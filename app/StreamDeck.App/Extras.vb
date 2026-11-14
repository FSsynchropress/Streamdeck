' Erweiterungen: Fenster verschieben/auf Monitor legen (Aktion window) und Betrieb mit Administratorrechten.
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports StreamDeck.Core

' ====================================================================== Fenster

''' <summary>Aktion window: Fenster eines Programms (oder das aktive Fenster) auf einen Monitor legen, maximieren usw.</summary>
Public Class WindowActionHandler
    Implements IActionHandler

    Private Const SW_MAXIMIZE As Integer = 3
    Private Const SW_MINIMIZE As Integer = 6
    Private Const SW_RESTORE As Integer = 9
    Private Const SWP_NOZORDER As UInteger = &H4
    Private Const SWP_NOACTIVATE As UInteger = &H10

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left, Top, Right, Bottom As Integer
    End Structure

    <DllImport("user32.dll")> Private Shared Function GetForegroundWindow() As IntPtr
    End Function
    <DllImport("user32.dll")> Private Shared Function ShowWindow(hWnd As IntPtr, cmd As Integer) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function IsZoomed(hWnd As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function
    <DllImport("user32.dll")> Private Shared Function GetWindowRect(hWnd As IntPtr, ByRef r As RECT) As Boolean
    End Function
    <DllImport("user32.dll", SetLastError:=True)> Private Shared Function SetWindowPos(hWnd As IntPtr, after As IntPtr, x As Integer, y As Integer, cx As Integer, cy As Integer, flags As UInteger) As Boolean
    End Function

    ''' <summary>Monitore in Windows-Nummerierung (\\.\DISPLAY1, DISPLAY2 …).</summary>
    Public Shared Function Monitors() As List(Of Screen)
        Return Screen.AllScreens.OrderBy(Function(s) DisplayNumber(s.DeviceName)).ThenBy(Function(s) s.Bounds.X).ToList()
    End Function

    Private Shared Function DisplayNumber(deviceName As String) As Integer
        Dim digits = New String(If(deviceName, "").Reverse().TakeWhile(AddressOf Char.IsDigit).Reverse().ToArray())
        Dim n As Integer
        Return If(Integer.TryParse(digits, n), n, Integer.MaxValue)
    End Function

    Private Shared Function ToRectI(r As Rectangle) As RectI
        Return New RectI(r.X, r.Y, r.Width, r.Height)
    End Function

    Private Shared Function FindWindow(path As String) As IntPtr
        If String.IsNullOrWhiteSpace(path) Then Return GetForegroundWindow()
        Dim name = ProcessActions.ProcessNameFromPath(path)
        For Each p In Process.GetProcessesByName(name)
            Try
                If p.MainWindowHandle <> IntPtr.Zero Then Return p.MainWindowHandle
            Finally
                p.Dispose()
            End Try
        Next
        Return IntPtr.Zero
    End Function

    Public Function ExecuteAsync(a As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Dim hwnd = FindWindow(a.Path)
        If hwnd = IntPtr.Zero Then Throw New InvalidOperationException(If(String.IsNullOrWhiteSpace(a.Path), "Kein aktives Fenster.", $"Kein Fenster von '{a.Path}' gefunden."))
        Dim op = If(a.Op, "monitor").Trim().ToLowerInvariant()
        Select Case op
            Case "maximize" : ShowWindow(hwnd, SW_MAXIMIZE)
            Case "minimize" : ShowWindow(hwnd, SW_MINIMIZE)
            Case "restore" : ShowWindow(hwnd, SW_RESTORE)
            Case "monitor", "nextmonitor"
                Dim screens = Monitors()
                Dim current = Screen.FromHandle(hwnd)
                Dim currentIndex = Math.Max(0, screens.FindIndex(Function(s) s.DeviceName = current.DeviceName))
                Dim targetIndex = If(op = "nextmonitor", (currentIndex + 1) Mod screens.Count,
                                     WindowLayout.MonitorIndex(a.Monitor.GetValueOrDefault(1), screens.Count))
                If targetIndex = currentIndex Then Return Task.CompletedTask
                Dim wasMax = IsZoomed(hwnd)
                If wasMax OrElse IsIconic(hwnd) Then ShowWindow(hwnd, SW_RESTORE)
                Dim r As RECT
                GetWindowRect(hwnd, r)
                Dim target = WindowLayout.MoveToArea(New RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top),
                                                     ToRectI(current.WorkingArea), ToRectI(screens(targetIndex).WorkingArea))
                Place(hwnd, target)
                If wasMax Then ShowWindow(hwnd, SW_MAXIMIZE)
            Case "move"
                Dim screens = Monitors()
                Dim area = If(a.Monitor.HasValue, screens(WindowLayout.MonitorIndex(a.Monitor.Value, screens.Count)), Screen.FromHandle(hwnd)).WorkingArea
                If IsZoomed(hwnd) OrElse IsIconic(hwnd) Then ShowWindow(hwnd, SW_RESTORE)
                Place(hwnd, WindowLayout.PercentOf(ToRectI(area), a.X.GetValueOrDefault(0), a.Y.GetValueOrDefault(0),
                                                   a.Width.GetValueOrDefault(50), a.Height.GetValueOrDefault(100)))
            Case Else
                Throw New ArgumentException($"Unbekannte Fenster-Aktion '{a.Op}'.")
        End Select
        Return Task.CompletedTask
    End Function

    Private Shared Sub Place(hwnd As IntPtr, r As RectI)
        If Not SetWindowPos(hwnd, IntPtr.Zero, r.X, r.Y, r.Width, r.Height, SWP_NOZORDER Or SWP_NOACTIVATE) Then
            Throw New ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Fenster lässt sich nicht verschieben (läuft das Programm als Administrator?).")
        End If
    End Sub
End Class

' ====================================================================== Administratorrechte

''' <summary>
''' Hotkeys erreichen Programme mit Administratorrechten nur, wenn die App selbst als Administrator läuft.
''' Autostart mit Administratorrechten geht nicht über den Run-Schlüssel (der startet ohne Rechte), sondern
''' über eine geplante Aufgabe mit "höchsten Privilegien" – die Windows dann ohne UAC-Abfrage startet.
''' </summary>
Public Module Elevation
    Public Const TaskName As String = "DIY StreamDeck (Administrator)"
    Private Const ErrorCancelled As Integer = 1223

    Public Function IsElevated() As Boolean
        Using id = WindowsIdentity.GetCurrent()
            Return New WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator)
        End Using
    End Function

    ''' <summary>
    ''' Startet die App mit Administratorrechten (UAC-Abfrage). Die neue Instanz wartet, bis diese beendet ist.
    ''' Liefert False, wenn die Abfrage abgelehnt wurde.
    ''' </summary>
    Public Function RestartElevated(startHidden As Boolean) As Boolean
        Dim psi As New ProcessStartInfo(Environment.ProcessPath) With {
            .UseShellExecute = True, .Verb = "runas",
            .Arguments = $"{If(startHidden, "--tray ", "")}--wait-for {Environment.ProcessId}"}
        Try
            Process.Start(psi)?.Dispose()
            Return True
        Catch ex As ComponentModel.Win32Exception When ex.NativeErrorCode = ErrorCancelled
            Return False
        End Try
    End Function

    ''' <summary>Programmstart mit "--wait-for &lt;pid&gt;": warten, bis die alte Instanz beendet ist (höchstens 15 s).</summary>
    Public Sub WaitForPreviousInstance(args As String())
        Dim i = Array.FindIndex(args, Function(x) x.Equals("--wait-for", StringComparison.OrdinalIgnoreCase))
        Dim pid As Integer
        If i < 0 OrElse i + 1 >= args.Length OrElse Not Integer.TryParse(args(i + 1), pid) Then Return
        Try
            Using p = Process.GetProcessById(pid)
                p.WaitForExit(15000)
            End Using
        Catch ex As ArgumentException
            ' schon beendet
        End Try
    End Sub

    Private Function RunSchtasks(arguments As String, elevate As Boolean) As Integer
        Dim psi As New ProcessStartInfo("schtasks.exe", arguments) With {.CreateNoWindow = True, .WindowStyle = ProcessWindowStyle.Hidden}
        If elevate AndAlso Not IsElevated() Then
            psi.UseShellExecute = True
            psi.Verb = "runas"
        Else
            psi.UseShellExecute = False
        End If
        Using p = Process.Start(psi)
            p.WaitForExit(15000)
            Return If(p.HasExited, p.ExitCode, -1)
        End Using
    End Function

    Public Function IsElevatedAutostartEnabled() As Boolean
        Try
            Return RunSchtasks($"/Query /TN ""{TaskName}""", elevate:=False) = 0
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Geplante Aufgabe "bei Anmeldung, höchste Privilegien" anlegen oder entfernen (UAC-Abfrage, falls nötig).</summary>
    Public Sub SetElevatedAutostart(enabled As Boolean)
        Dim code As Integer
        If enabled Then
            Dim tr = $"\""{Environment.ProcessPath}\"" --tray"
            code = RunSchtasks($"/Create /F /TN ""{TaskName}"" /SC ONLOGON /RL HIGHEST /DELAY 0000:10 /TR ""{tr}""", elevate:=True)
        Else
            If Not IsElevatedAutostartEnabled() Then Return
            code = RunSchtasks($"/Delete /F /TN ""{TaskName}""", elevate:=True)
        End If
        If code <> 0 Then Throw New InvalidOperationException($"Geplante Aufgabe konnte nicht {If(enabled, "angelegt", "entfernt")} werden (Code {code}).")
    End Sub
End Module
