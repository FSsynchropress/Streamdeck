' Eigenen Sound einsprechen: Mikrofon aufnehmen, anhören, Stille abschneiden, Lautstärke angleichen,
' als WAV in %APPDATA%\StreamDeck\sounds speichern.
Imports System.Drawing.Drawing2D
Imports NAudio.CoreAudioApi
Imports NAudio.Wave
Imports StreamDeck.Core

Public Class RecorderForm
    Inherits ThemedForm

    Private Const MaxSeconds As Integer = 30
    Private Const DefaultMic As String = "Standard-Mikrofon"

    Private ReadOnly _mic As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Width = 330}
    Private ReadOnly _record As New ThemedButton("Aufnehmen", ButtonKind.Danger, ChrW(&HE7C8)) With {.BaseHeight = 40}
    Private ReadOnly _play As New ThemedButton("Anhören", ButtonKind.Secondary, Theme.GlyphPlay) With {.BaseHeight = 40, .Enabled = False}
    Private ReadOnly _time As New Label With {.AutoSize = True, .Font = Theme.Mono(14.0F), .Text = "0:00,0"}
    Private ReadOnly _meter As New LevelMeter With {.Height = 10, .Dock = DockStyle.Top}
    Private ReadOnly _status As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .MaximumSize = New Size(470, 0)}
    Private ReadOnly _trim As New CheckBox With {.Text = "Stille am Anfang und Ende abschneiden", .AutoSize = True, .Checked = True}
    Private ReadOnly _normalize As New CheckBox With {.Text = "Lautstärke angleichen (leise Aufnahmen lauter machen)", .AutoSize = True, .Checked = True}
    Private ReadOnly _name As New TextBox With {.Width = 330}
    Private ReadOnly _ok As New ThemedButton("Speichern", ButtonKind.Primary) With {.Enabled = False}
    Private ReadOnly _ui As New Timer With {.Interval = 40}

    Private _capture As WasapiCapture
    Private _sampleRate As Integer
    Private ReadOnly _samples As New List(Of Single)   ' Mono
    Private ReadOnly _lock As New Object()
    Private _peak As Single
    Private _recording As Boolean
    Private _player As WaveOutEvent

    ''' <summary>Pfad für die Konfiguration (z. B. "%APPDATA%\StreamDeck\sounds\Hallo.wav").</summary>
    Public Property Result As String

    Public Sub New()
        Text = "Eigenen Sound aufnehmen"
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        MinimizeBox = False
        MaximizeBox = False
        FormBorderStyle = FormBorderStyle.FixedDialog
        ClientSize = New Size(540, 390)

        _mic.Items.Add(DefaultMic)
        For Each d In MicActionHandler.GetInputDevices()
            _mic.Items.Add(d)
        Next
        _mic.SelectedIndex = 0
        AddHandler _mic.SelectedIndexChanged, Sub() CheckMuted()

        _name.Text = "Aufnahme " & DateTime.Now.ToString("yyyy-MM-dd HH-mm")

        Dim body As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 2, .Padding = New Padding(20, 18, 20, 8)}
        body.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        body.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        body.Controls.Add(RowLabel("Mikrofon:"), 0, 0)
        body.Controls.Add(_mic, 1, 0)

        Dim buttons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = New Padding(0, 14, 0, 6)}
        _record.Margin = New Padding(0, 0, 8, 0)
        buttons.Controls.Add(_record)
        buttons.Controls.Add(_play)
        _time.Margin = New Padding(14, 8, 0, 0)
        buttons.Controls.Add(_time)
        body.Controls.Add(buttons, 0, 1)
        body.SetColumnSpan(buttons, 2)

        Dim meterHost As New Panel With {.Height = 12, .Dock = DockStyle.Fill, .Margin = New Padding(0, 4, 0, 6)}
        meterHost.Controls.Add(_meter)
        body.Controls.Add(meterHost, 0, 2)
        body.SetColumnSpan(meterHost, 2)

        _status.Margin = New Padding(0, 0, 0, 10)
        body.Controls.Add(_status, 0, 3)
        body.SetColumnSpan(_status, 2)

        body.Controls.Add(_trim, 0, 4)
        body.SetColumnSpan(_trim, 2)
        body.Controls.Add(_normalize, 0, 5)
        body.SetColumnSpan(_normalize, 2)

        Dim nameLabel = RowLabel("Name:")
        nameLabel.Margin = New Padding(0, 16, 8, 0)
        _name.Margin = New Padding(0, 12, 0, 0)
        body.Controls.Add(nameLabel, 0, 6)
        body.Controls.Add(_name, 1, 6)

        Dim cancel As New ThemedButton("Abbrechen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}
        Dim hint As New Label With {.Text = $"Max. {MaxSeconds} s · Leertaste = Start/Stopp", .AutoSize = True,
                                    .ForeColor = Theme.Faint, .Font = Theme.Ui(8.5F), .Padding = New Padding(0, 9, 0, 0)}

        Controls.Add(body)
        Controls.Add(MakeFooter(hint, cancel, _ok))
        CancelButton = cancel
        Theme.Apply(Me)

        AddHandler _record.Click, Sub() ToggleRecording()
        AddHandler _play.Click, Sub() TogglePlayback()
        AddHandler _trim.CheckedChanged, Sub() UpdateResultInfo()
        AddHandler _normalize.CheckedChanged, Sub() StopPlayback()
        AddHandler _ok.Click, Sub() Save()
        AddHandler _ui.Tick, Sub() OnUiTick()
        AddHandler Shown, Sub()
                              CheckMuted()
                              _record.Focus()
                          End Sub
        SetStatus("Auf 'Aufnehmen' klicken und lossprechen.", Theme.Muted)
    End Sub

    Private Shared Function RowLabel(text As String) As Label
        Return New Label With {.Text = text, .AutoSize = True, .ForeColor = Theme.Muted, .Margin = New Padding(0, 4, 8, 0)}
    End Function

    Private Sub SetStatus(text As String, color As Color)
        _status.Text = text
        _status.ForeColor = color
    End Sub

    ' ------------------------------------------------------------------ Gerät

    Private Function OpenDevice(en As MMDeviceEnumerator) As MMDevice
        Dim name = CStr(_mic.SelectedItem)
        If name = DefaultMic Then Return en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
        For Each d In en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            If d.FriendlyName = name Then Return d
            d.Dispose()
        Next
        Throw New InvalidOperationException($"Mikrofon '{name}' nicht gefunden.")
    End Function

    ''' <summary>Warnt, wenn das gewählte Mikrofon stummgeschaltet ist (z. B. per Mikrofon-Taste am Deck).</summary>
    Private Sub CheckMuted()
        If _recording Then Return
        Try
            Using en As New MMDeviceEnumerator(), d = OpenDevice(en)
                If d.AudioEndpointVolume.Mute Then
                    SetStatus("Achtung: Dieses Mikrofon ist stummgeschaltet – die Aufnahme bliebe leer. Vorher die Stummschaltung aufheben.", Theme.BadText)
                ElseIf _samples.Count = 0 Then
                    SetStatus("Auf 'Aufnehmen' klicken und lossprechen.", Theme.Muted)
                End If
            End Using
        Catch ex As Exception
            SetStatus("Kein Mikrofon gefunden: " & ex.Message, Theme.BadText)
        End Try
    End Sub

    ' ------------------------------------------------------------------ Aufnahme

    Private Sub ToggleRecording()
        If _recording Then
            StopRecording()
        Else
            StartRecording()
        End If
    End Sub

    Private Sub StartRecording()
        StopPlayback()
        Dim device As MMDevice = Nothing
        Try
            Using en As New MMDeviceEnumerator()
                device = OpenDevice(en)
            End Using
            Dim cap As New WasapiCapture(device, True, 20)
            Dim wf = cap.WaveFormat
            _sampleRate = wf.SampleRate
            SyncLock _lock
                _samples.Clear()
                _peak = 0
            End SyncLock
            Dim dev = device
            AddHandler cap.DataAvailable, Sub(s, e) OnData(wf, e.Buffer, e.BytesRecorded)
            AddHandler cap.RecordingStopped, Sub(s, e)
                                                 cap.Dispose()
                                                 dev.Dispose()
                                                 If e.Exception IsNot Nothing Then
                                                     BeginInvokeSafe(Sub() SetStatus("Aufnahme abgebrochen: " & e.Exception.Message, Theme.BadText))
                                                 End If
                                             End Sub
            cap.StartRecording()
            _capture = cap
        Catch ex As Exception
            device?.Dispose()
            SetStatus("Aufnahme nicht möglich: " & ex.Message, Theme.BadText)
            Return
        End Try
        _recording = True
        _record.Text = "Stopp"
        _record.Kind = ButtonKind.Secondary
        _mic.Enabled = False
        _ok.Enabled = False
        _play.Enabled = False
        SetStatus("Aufnahme läuft … jetzt sprechen.", Theme.BadText)
        _ui.Start()
    End Sub

    ''' <summary>Läuft im Aufnahme-Thread: Bytes → Mono-Samples.</summary>
    Private Sub OnData(wf As WaveFormat, buffer As Byte(), count As Integer)
        Dim bytesPer = wf.BitsPerSample \ 8
        Dim frame = bytesPer * wf.Channels
        If frame = 0 Then Return
        Dim isFloat = wf.Encoding = WaveFormatEncoding.IeeeFloat OrElse (wf.Encoding = WaveFormatEncoding.Extensible AndAlso wf.BitsPerSample = 32)
        Dim frames = count \ frame
        SyncLock _lock
            Dim limit = MaxSeconds * wf.SampleRate
            For f = 0 To frames - 1
                If _samples.Count >= limit Then Exit For
                Dim sum = 0.0F
                For c = 0 To wf.Channels - 1
                    Dim o = f * frame + c * bytesPer
                    Dim v As Single
                    Select Case bytesPer
                        Case 4 : v = If(isFloat, BitConverter.ToSingle(buffer, o), CSng(BitConverter.ToInt32(buffer, o) / 2147483648.0))
                        Case 3 : v = CSng(((CInt(buffer(o + 2)) << 24) Or (CInt(buffer(o + 1)) << 16) Or (CInt(buffer(o)) << 8)) / 2147483648.0)
                        Case 2 : v = BitConverter.ToInt16(buffer, o) / 32768.0F
                        Case Else : v = (CInt(buffer(o)) - 128) / 128.0F
                    End Select
                    sum += v
                Next
                Dim m = sum / wf.Channels
                _samples.Add(m)
                Dim a = Math.Abs(m)
                If a > _peak Then _peak = a
            Next
        End SyncLock
    End Sub

    Private Sub OnUiTick()
        Dim n As Integer, peak As Single
        SyncLock _lock
            n = _samples.Count
            peak = _peak
            _peak = 0
        End SyncLock
        _meter.Push(If(_recording, peak, 0))
        If _recording AndAlso _sampleRate > 0 Then
            _time.Text = FormatTime(n / _sampleRate)
            If n >= MaxSeconds * _sampleRate Then StopRecording()
        ElseIf Not _recording AndAlso _meter.Level <= 0.001F Then
            _ui.Stop()
        End If
    End Sub

    Private Sub StopRecording()
        If Not _recording Then Return
        _recording = False
        Try
            _capture?.StopRecording()
        Catch
        End Try
        _capture = Nothing
        _record.Text = "Neu aufnehmen"
        _record.Kind = ButtonKind.Danger
        _mic.Enabled = True
        UpdateResultInfo()
        _record.Focus()
    End Sub

    Private Sub UpdateResultInfo()
        StopPlayback()
        If _recording Then Return
        Dim count As Integer
        SyncLock _lock
            count = _samples.Count
        End SyncLock
        If count = 0 OrElse _sampleRate = 0 Then
            _ok.Enabled = False
            _play.Enabled = False
            Return
        End If
        Dim processed = ProcessSamples()
        If processed.Length = 0 Then
            SetStatus("Es war nichts zu hören. Ist das richtige Mikrofon gewählt und nicht stummgeschaltet?", Theme.BadText)
            _ok.Enabled = False
            _play.Enabled = False
            _time.Text = FormatTime(count / _sampleRate)
            Return
        End If
        _time.Text = FormatTime(processed.Length / _sampleRate)
        SetStatus($"Fertig: {FormatSeconds(processed.Length / _sampleRate)} Sekunden. Mit 'Anhören' prüfen, dann speichern.", Theme.GoodText)
        _ok.Enabled = True
        _play.Enabled = True
    End Sub

    ''' <summary>Aufnahme mit den gewählten Nachbearbeitungen.</summary>
    Private Function ProcessSamples() As Single()
        Dim raw As Single()
        SyncLock _lock
            raw = _samples.ToArray()
        End SyncLock
        Dim result As Single()
        If _trim.Checked Then
            Dim r = RecordingTools.FindSoundRange(raw, _sampleRate)
            If r.Length = 0 Then Return Array.Empty(Of Single)()
            result = New Single(r.Length - 1) {}
            Array.Copy(raw, r.Start, result, 0, r.Length)
        Else
            result = raw
        End If
        If _normalize.Checked Then RecordingTools.Normalize(result)
        RecordingTools.FadeEdges(result, _sampleRate)
        Return result
    End Function

    Private Shared Function FormatTime(seconds As Double) As String
        Dim t = TimeSpan.FromSeconds(seconds)
        Return $"{CInt(Math.Floor(t.TotalMinutes))}:{t.Seconds:00},{t.Milliseconds \ 100}"
    End Function

    Private Shared Function FormatSeconds(seconds As Double) As String
        Return seconds.ToString("0.0", Globalization.CultureInfo.GetCultureInfo("de-DE"))
    End Function

    ' ------------------------------------------------------------------ Anhören

    Private Sub TogglePlayback()
        If _player IsNot Nothing Then
            StopPlayback()
            Return
        End If
        Dim samples = ProcessSamples()
        If samples.Length = 0 Then Return
        Try
            Dim bytes(samples.Length * 4 - 1) As Byte
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length)
            Dim stream As New RawSourceWaveStream(New IO.MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 1))
            Dim player As New WaveOutEvent()
            player.Init(stream)
            AddHandler player.PlaybackStopped, Sub()
                                                   stream.Dispose()
                                                   BeginInvokeSafe(Sub()
                                                                       If _player Is player Then StopPlayback()
                                                                   End Sub)
                                               End Sub
            _player = player
            player.Play()
            _play.Text = "Stopp"
        Catch ex As Exception
            StopPlayback()
            SetStatus("Abspielen nicht möglich: " & ex.Message, Theme.BadText)
        End Try
    End Sub

    Private Sub StopPlayback()
        Dim p = _player
        _player = Nothing
        _play.Text = "Anhören"
        Try
            p?.Stop()
        Catch
        End Try
        p?.Dispose()
    End Sub

    ' ------------------------------------------------------------------ Speichern

    Private Sub Save()
        StopPlayback()
        Dim samples = ProcessSamples()
        If samples.Length = 0 Then Return
        Try
            IO.Directory.CreateDirectory(RecordingTools.Folder)
            Dim path = RecordingTools.UniquePath(RecordingTools.Folder, _name.Text, ".wav")
            Using w As New WaveFileWriter(path, New WaveFormat(_sampleRate, 16, 1))
                w.WriteSamples(samples, 0, samples.Length)
            End Using
            Result = RecordingTools.ConfigPath(path)
            DialogResult = DialogResult.OK
        Catch ex As Exception
            MessageBox.Show(Me, $"Speichern nicht möglich:{vbCrLf}{ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
        End Try
    End Sub

    ' ------------------------------------------------------------------ Allgemein

    Protected Overrides Function ProcessCmdKey(ByRef msg As Message, keyData As Keys) As Boolean
        If keyData = Keys.Space AndAlso Not _name.Focused AndAlso Not _mic.Focused Then
            ToggleRecording()
            Return True
        End If
        Return MyBase.ProcessCmdKey(msg, keyData)
    End Function

    Private Sub BeginInvokeSafe(a As Action)
        If IsDisposed OrElse Not IsHandleCreated Then Return
        Try
            BeginInvoke(a)
        Catch ex As InvalidOperationException
        End Try
    End Sub

    Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
        If _recording Then StopRecording()
        StopPlayback()
        _ui.Stop()
        MyBase.OnFormClosing(e)
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _ui.Dispose()
        MyBase.Dispose(disposing)
    End Sub

    ''' <summary>Öffnet den Aufnahme-Dialog. Nothing = abgebrochen.</summary>
    Public Shared Function Record(owner As IWin32Window) As String
        Using d As New RecorderForm()
            Return If(d.ShowDialog(owner) = DialogResult.OK, d.Result, Nothing)
        End Using
    End Function
End Class

' ====================================================================== Pegelanzeige

''' <summary>Waagerechter Pegelbalken (grün → bernstein → rot), fällt langsam ab.</summary>
Public Class LevelMeter
    Inherits Control

    Private _level As Single

    Public Sub New()
        SetStyle(ControlStyles.UserPaint Or ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.ResizeRedraw, True)
    End Sub

    Public ReadOnly Property Level As Single
        Get
            Return _level
        End Get
    End Property

    ''' <summary>Neuer Spitzenwert 0…1; Anzeige in dB (-60…0), mit Abfall.</summary>
    Public Sub Push(peak As Single)
        Dim db = If(peak > 0, 20 * Math.Log10(peak), -60.0)
        Dim v = CSng(Math.Max(0, Math.Min(1, (db + 60) / 60)))
        _level = Math.Max(v, _level - 0.04F)
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent?.BackColor, Theme.Surface))
        Dim r As New Rectangle(0, 0, Width - 1, Height - 1)
        Using bg As New SolidBrush(Theme.InputBg), path = Rounded(r, Height \ 2)
            g.FillPath(bg, path)
        End Using
        Dim w = CInt(r.Width * _level)
        If w > Height Then
            Using brush As New LinearGradientBrush(r, Theme.Good, Theme.Bad, LinearGradientMode.Horizontal)
                brush.InterpolationColors = New ColorBlend(3) With {
                    .Colors = {Theme.Good, Theme.Accent, Theme.Bad},
                    .Positions = {0.0F, 0.75F, 1.0F}}
                Using path = Rounded(New Rectangle(0, 0, w, r.Height), Height \ 2)
                    g.FillPath(brush, path)
                End Using
            End Using
        End If
    End Sub

    Private Shared Function Rounded(r As Rectangle, radius As Integer) As GraphicsPath
        Dim p As New GraphicsPath()
        Dim d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)))
        p.AddArc(r.X, r.Y, d, d, 90, 180)
        p.AddArc(r.Right - d, r.Y, d, d, 270, 180)
        p.CloseFigure()
        Return p
    End Function
End Class
