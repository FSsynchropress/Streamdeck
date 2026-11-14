' Programmstart und Betrieb im Infobereich (Tray). Das Hauptfenster ist optional – das Deck
' funktioniert, solange dieser Kontext läuft.
Imports System.Drawing.Drawing2D
Imports System.Runtime.InteropServices
Imports System.Threading
Imports StreamDeck.Core

Public Module Program
    Private Const InstanceName As String = "Local\DIY-StreamDeck-App"

    <STAThread>
    Public Sub Main(args As String())
        ' Muss als Erstes laufen: bearbeitet Installations-/Update-Aufrufe des Setups und beendet sich dann ggf. sofort.
        Velopack.VelopackApp.Build() _
            .OnBeforeUninstallFastCallback(Sub(v) Autostart.SetEnabled(False)) _
            .Run()

        Dim createdNew As Boolean
        Using mutex As New Mutex(True, InstanceName, createdNew)
            If Not createdNew Then
                ' Läuft schon: dort das Fenster öffnen und selbst beenden (nur eine App darf den COM-Port nutzen).
                Try
                    Using ev = EventWaitHandle.OpenExisting(InstanceName & "-show")
                        ev.Set()
                    End Using
                Catch
                End Try
                Return
            End If

            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            ToolStripManager.Renderer = New DarkMenuRenderer()   ' alle Kontextmenüs im dunklen Design
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)
            AddHandler Application.ThreadException,
                Sub(s, e) MessageBox.Show(e.Exception.Message, "DIY Stream Deck – Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error)

            Dim path = ConfigStore.DefaultPath
            Dim cfg = LoadConfig(path)
            If cfg Is Nothing Then Return

            Dim startHidden = args.Any(Function(a) a.Equals("--tray", StringComparison.OrdinalIgnoreCase))
            Using showEvent As New EventWaitHandle(False, EventResetMode.AutoReset, InstanceName & "-show")
                Using ctx As New TrayContext(cfg, path, showEvent, startHidden)
                    Application.Run(ctx)
                End Using
            End Using
        End Using
    End Sub

    ''' <summary>Lädt die Konfiguration; ist sie kaputt, wird gefragt, ob neu angelegt werden soll.</summary>
    Private Function LoadConfig(path As String) As DeckConfig
        Try
            Return ConfigStore.Load(path)
        Catch ex As Exception
            Dim answer = MessageBox.Show(
                $"Die Konfiguration kann nicht gelesen werden:{vbCrLf}{path}{vbCrLf}{vbCrLf}{ex.Message}{vbCrLf}{vbCrLf}" &
                "Soll eine neue Standard-Konfiguration angelegt werden? Die alte Datei wird vorher gesichert.",
                "DIY Stream Deck", MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
            If answer <> DialogResult.Yes Then Return Nothing
            Try
                IO.File.Move(path, IO.Path.ChangeExtension(path, $".broken-{Date.Now:yyyyMMdd-HHmmss}.json"))
                Return ConfigStore.Load(path)
            Catch ex2 As Exception
                MessageBox.Show(ex2.Message, "DIY Stream Deck", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return Nothing
            End Try
        End Try
    End Function
End Module

Public Class TrayContext
    Inherits ApplicationContext

    Public Const MaxLogLines As Integer = 500

    Private ReadOnly _configPath As String
    Private ReadOnly _invoker As Control
    Private ReadOnly _notify As NotifyIcon
    Private ReadOnly _runner As ActionRunner
    Private ReadOnly _sound As SoundActionHandler
    Private ReadOnly _obs As ObsActionHandler
    Private ReadOnly _foregroundTimer As System.Windows.Forms.Timer
    Private ReadOnly _waitHandle As RegisteredWaitHandle
    Private ReadOnly _autostartItem As ToolStripMenuItem
    Private ReadOnly _updateItem As ToolStripMenuItem
    Private ReadOnly _logLines As New List(Of String)
    Private ReadOnly _fileLog As New FileLog(FileLog.DefaultFolder)
    Private _form As MainForm
    Private _lastForeground As String
    Private _exiting As Boolean
    Private _hintShown As Boolean

    Public ReadOnly Property Engine As DeckEngine
    Public ReadOnly Property Connection As DeckConnection
    Public ReadOnly Property Updates As Updater
    Public ReadOnly Property IconConnected As Icon
    Public ReadOnly Property IconDisconnected As Icon

    ''' <summary>Neue Protokollzeile (immer auf dem UI-Thread).</summary>
    Public Event LogAdded(line As String)
    ''' <summary>Verbunden/getrennt (immer auf dem UI-Thread).</summary>
    Public Event ConnectionChanged()

    Public Sub New(config As DeckConfig, configPath As String, showEvent As EventWaitHandle, startHidden As Boolean)
        _configPath = configPath
        _invoker = New Control()
        _invoker.CreateControl()   ' Fenster-Handle, um von Hintergrund-Threads auf den UI-Thread zu wechseln

        IconConnected = MakeStatusIcon(Theme.Good, connected:=True)
        IconDisconnected = MakeStatusIcon(Theme.Bad, connected:=False)

        ' Aktionen
        _runner = New ActionRunner()
        ProcessActions.Register(_runner, AddressOf Log)
        NativeInput.Register(_runner)
        _sound = New SoundActionHandler(AddressOf Log)
        _runner.Register(ActionTypes.Sound, _sound)
        Engine = New DeckEngine(config, _runner)
        _obs = New ObsActionHandler(Function() Engine.Config.Obs)
        _runner.Register(ActionTypes.Obs, _obs)
        HttpActions.Register(_runner)
        _runner.Register(ActionTypes.Mic, New MicActionHandler(_runner, AddressOf Log))
        AddHandler _runner.Notify, Sub(t, m) Post(Sub() ShowHint(t, m))
        AddHandler Engine.Log, AddressOf Log
        AddHandler Engine.ConfigChanged, Sub() Post(AddressOf SaveConfig)

        ' Gerät
        Connection = New DeckConnection(New SerialPortLinkFactory())
        Engine.Attach(Connection)
        AddHandler Connection.Connected, Sub(p) Post(AddressOf OnConnectionChanged)
        AddHandler Connection.Disconnected, Sub() Post(AddressOf OnConnectionChanged)

        ' Tray
        Dim menu As New ContextMenuStrip()
        Dim openItem = menu.Items.Add("Fenster öffnen", Nothing, Sub() ShowMainForm())
        openItem.Font = New Font(openItem.Font, FontStyle.Bold)
        _autostartItem = New ToolStripMenuItem("Mit Windows starten", Nothing, Sub() ToggleAutostart())
        menu.Items.Add(_autostartItem)
        menu.Items.Add("Protokollordner öffnen", Nothing, Sub() OpenLogFolder())
        menu.Items.Add(New ToolStripSeparator())

        ' Updates
        Updates = New Updater(AddressOf Log)
        menu.Items.Add(New ToolStripMenuItem($"Version {Updates.CurrentVersion}") With {.Enabled = False})
        _updateItem = New ToolStripMenuItem("Nach Updates suchen", Nothing, Sub() OnUpdateMenuClick())
        menu.Items.Add(_updateItem)
        menu.Items.Add(New ToolStripSeparator())
        menu.Items.Add("Beenden", Nothing, Sub() ExitApp())
        AddHandler menu.Opening, Sub() _autostartItem.Checked = SafeAutostartEnabled()
        AddHandler Updates.UpdateReady, Sub(v) Post(Sub() OnUpdateReady(v))

        _notify = New NotifyIcon With {.Icon = IconDisconnected, .Text = "DIY Stream Deck – nicht verbunden", .ContextMenuStrip = menu, .Visible = True}
        AddHandler _notify.MouseClick, Sub(s, e)
                                           If e.Button = MouseButtons.Left Then ShowMainForm()
                                       End Sub

        ' Automatische Profilwahl nach Vordergrundprogramm
        _foregroundTimer = New System.Windows.Forms.Timer With {.Interval = 500}
        AddHandler _foregroundTimer.Tick, Sub() CheckForeground()
        _foregroundTimer.Start()

        ' Zweiter Programmstart -> Fenster zeigen
        _waitHandle = ThreadPool.RegisterWaitForSingleObject(showEvent, Sub(s, t) Post(AddressOf ShowMainForm), Nothing, -1, False)

        Autostart.RefreshPathIfEnabled()
        Log($"DIY Stream Deck {Updates.CurrentVersion}")
        Log($"Konfiguration: {configPath}")
        Connection.Start()
        Updates.Start()
        If Not startHidden Then ShowMainForm()
    End Sub

    ' ------------------------------------------------------------------ Hilfen

    ''' <summary>Führt die Aktion auf dem UI-Thread aus (von jedem Thread aus aufrufbar).</summary>
    Public Sub Post(action As Action)
        Try
            If Not _invoker.IsDisposed AndAlso _invoker.IsHandleCreated Then _invoker.BeginInvoke(action)
        Catch ex As InvalidOperationException   ' auch ObjectDisposedException: Programm wird gerade beendet
        End Try
    End Sub

    ''' <summary>Protokolliert eine Meldung (von jedem Thread aus aufrufbar).</summary>
    Public Sub Log(message As String)
        _fileLog.Write(message)
        Dim line = $"{Date.Now:HH:mm:ss}  {message}"
        Post(Sub()
                 _logLines.Add(line)
                 If _logLines.Count > MaxLogLines Then _logLines.RemoveRange(0, _logLines.Count - MaxLogLines)
                 RaiseEvent LogAdded(line)
             End Sub)
    End Sub

    Public ReadOnly Property LogLines As IReadOnlyList(Of String)
        Get
            Return _logLines
        End Get
    End Property

    Public ReadOnly Property IsExiting As Boolean
        Get
            Return _exiting
        End Get
    End Property

    Public ReadOnly Property ConfigPath As String
        Get
            Return _configPath
        End Get
    End Property

    ''' <summary>Kurzer Hinweis am Tray-Symbol (bis das On-Screen-Overlay kommt).</summary>
    Private Sub ShowHint(title As String, message As String)
        If _exiting OrElse _notify Is Nothing Then Return
        _notify.ShowBalloonTip(2000, title, If(String.IsNullOrEmpty(message), " ", message), ToolTipIcon.None)
    End Sub

    Private Sub OpenLogFolder()
        Try
            IO.Directory.CreateDirectory(_fileLog.Folder)
            Process.Start(New ProcessStartInfo(_fileLog.Folder) With {.UseShellExecute = True})?.Dispose()
        Catch ex As Exception
            Log($"Protokollordner: {ex.Message}")
        End Try
    End Sub

    Private Sub SaveConfig()
        Try
            ConfigStore.Save(Engine.Config, _configPath)
        Catch ex As Exception
            Log($"Speichern fehlgeschlagen: {ex.Message}")
        End Try
    End Sub

    Private Sub OnConnectionChanged()
        If _exiting Then Return
        Dim port = Connection.PortName
        If port IsNot Nothing Then
            _notify.Icon = IconConnected
            _notify.Text = $"DIY Stream Deck – verbunden ({port})"
        Else
            _notify.Icon = IconDisconnected
            _notify.Text = "DIY Stream Deck – nicht verbunden"
        End If
        RaiseEvent ConnectionChanged()
    End Sub

    Private Sub CheckForeground()
        If Not Engine.Config.Profiles.Any(Function(p) p.AutoSwitchProcess IsNot Nothing) Then
            _lastForeground = Nothing
            Return
        End If
        Dim name = ForegroundProcess.GetName()
        If name Is Nothing OrElse String.Equals(name, _lastForeground, StringComparison.OrdinalIgnoreCase) Then Return
        _lastForeground = name
        ' Im eigenen Fenster nicht umschalten, sonst springt das Profil beim Bearbeiten weg.
        If String.Equals(name, Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase) Then Return
        If Engine.ApplyForegroundProcess(name) Then
            Log($"Profil '{Engine.CurrentProfile.Name}'{If(Engine.IsAutoProfile, $" (automatisch für {name})", "")}")
        End If
    End Sub

    Private Shared Function SafeAutostartEnabled() As Boolean
        Try
            Return Autostart.IsEnabled()
        Catch
            Return False
        End Try
    End Function

    Private Sub ToggleAutostart()
        Try
            Autostart.SetEnabled(Not Autostart.IsEnabled())
            Log(If(Autostart.IsEnabled(), "Autostart eingeschaltet", "Autostart ausgeschaltet"))
        Catch ex As Exception
            Log($"Autostart: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Übernimmt die Werte aus dem Einstellungsdialog.</summary>
    Public Sub ApplySettings(longPressMs As Integer, obsUrl As String, obsPassword As String, autostartEnabled As Boolean)
        Engine.UpdateConfig(Sub(c)
                                c.LongPressMs = longPressMs
                                c.Obs = New ObsSettings With {.Url = obsUrl, .Password = obsPassword}
                            End Sub)
        _obs.Reset()
        Try
            If autostartEnabled <> Autostart.IsEnabled() Then Autostart.SetEnabled(autostartEnabled)
        Catch ex As Exception
            Log($"Autostart: {ex.Message}")
        End Try
    End Sub

    ' ------------------------------------------------------------------ Fenster

    Public Sub ShowMainForm()
        If _exiting Then Return
        If _form Is Nothing OrElse _form.IsDisposed Then _form = New MainForm(Me)
        _form.Show()
        If _form.WindowState = FormWindowState.Minimized Then _form.WindowState = FormWindowState.Normal
        _form.Activate()
    End Sub

    ''' <summary>Hauptfenster wurde geschlossen (versteckt) – einmalig darauf hinweisen, dass die App weiterläuft.</summary>
    Public Sub OnMainFormHidden()
        If _hintShown Then Return
        _hintShown = True
        _notify.ShowBalloonTip(3000, "DIY Stream Deck", "Läuft im Infobereich weiter. Beenden über das Symbol (Rechtsklick).", ToolTipIcon.Info)
    End Sub

    ' ------------------------------------------------------------------ Updates

    Private Async Sub OnUpdateMenuClick()
        If Updates.ReadyVersion IsNot Nothing Then
            InstallUpdate()
            Return
        End If
        _updateItem.Enabled = False
        _updateItem.Text = "Suche nach Updates …"
        Dim message = Await Updates.CheckAsync(manual:=True)
        _updateItem.Enabled = True
        If Updates.ReadyVersion Is Nothing Then
            _updateItem.Text = "Nach Updates suchen"
            _notify.ShowBalloonTip(4000, "DIY Stream Deck", message, ToolTipIcon.Info)
        End If
    End Sub

    Private Sub OnUpdateReady(version As String)
        If _exiting Then Return
        _updateItem.Text = $"Update auf {version} installieren (Neustart)"
        _updateItem.Font = New Font(_updateItem.Font, FontStyle.Bold)
        _notify.ShowBalloonTip(5000, "DIY Stream Deck – Update bereit",
                               $"Version {version} ist heruntergeladen. Installieren über das Symbol (Rechtsklick) – sonst automatisch beim nächsten Start.",
                               ToolTipIcon.Info)
    End Sub

    ''' <summary>Gibt Gerät und Sound frei, installiert das Update und startet neu.</summary>
    Public Sub InstallUpdate()
        If Updates.ReadyVersion Is Nothing Then Return
        Shutdown()
        Updates.ApplyAndRestart()   ' beendet den Prozess
    End Sub

    Public Sub ExitApp()
        If _exiting Then Return
        Shutdown()
        ExitThread()
    End Sub

    Private Sub Shutdown()
        If _exiting Then Return
        _exiting = True
        Updates.Stop()
        _foregroundTimer.Stop()
        _waitHandle.Unregister(Nothing)
        _notify.Visible = False
        If _form IsNot Nothing AndAlso Not _form.IsDisposed Then _form.Close()
        Connection.Stop()
        Engine.Dispose()
        _sound.Dispose()
        _obs.Dispose()
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            _notify.Dispose()
            _foregroundTimer.Dispose()
            _invoker.Dispose()
        End If
        MyBase.Dispose(disposing)
    End Sub

    ' ------------------------------------------------------------------ Symbol

    <DllImport("user32.dll")>
    Private Shared Function DestroyIcon(hIcon As IntPtr) As Boolean
    End Function

    ''' <summary>Tastenfeld-Symbol (Bernstein = verbunden, grau = getrennt) mit Statuspunkt.</summary>
    Public Shared Function MakeStatusIcon(status As Color, connected As Boolean) As Icon
        Const S As Integer = 32
        Using bmp As New Bitmap(S, S)
            Using g = Graphics.FromImage(bmp)
                g.SmoothingMode = SmoothingMode.AntiAlias
                Dim bodyColor = If(connected, Theme.Accent, Color.FromArgb(94, 100, 108))
                Using body = RoundedRect(New RectangleF(1, 4, S - 4, S - 9), 5), bg As New SolidBrush(bodyColor)
                    g.FillPath(bg, body)
                End Using
                ' 3 × 5 Tasten
                Using keyBrush As New SolidBrush(Theme.AccentText)
                    For r = 0 To 2
                        For c = 0 To 4
                            g.FillRectangle(keyBrush, 4 + c * 4.8F, 7.5F + r * 5.0F, 3.4F, 3.4F)
                        Next
                    Next
                End Using
                Using dot As New SolidBrush(status), ring As New Pen(Theme.Chrome, 2.0F)
                    g.FillEllipse(dot, S - 13, S - 13, 12, 12)
                    g.DrawEllipse(ring, S - 13, S - 13, 12, 12)
                End Using
            End Using
            Dim h = bmp.GetHicon()
            Try
                Using tmp = Icon.FromHandle(h)
                    Return CType(tmp.Clone(), Icon)
                End Using
            Finally
                DestroyIcon(h)
            End Try
        End Using
    End Function

    Public Shared Function RoundedRect(r As RectangleF, radius As Single) As GraphicsPath
        Dim d = radius * 2
        Dim p As New GraphicsPath()
        p.AddArc(r.X, r.Y, d, d, 180, 90)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90)
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90)
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90)
        p.CloseFigure()
        Return p
    End Function
End Class
