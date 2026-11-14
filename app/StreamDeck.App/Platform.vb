' Windows-spezifische Bausteine: serieller Port, Tastatureingaben (SendInput), Autostart, Vordergrundprozess.
Imports System.IO.Ports
Imports System.Runtime.InteropServices
Imports System.Threading
Imports Microsoft.Win32
Imports StreamDeck.Core

' ====================================================================== Serieller Port

Public Class SerialPortLinkFactory
    Implements ISerialLinkFactory

    Public Function GetPortNames() As String() Implements ISerialLinkFactory.GetPortNames
        Return SerialPort.GetPortNames()
    End Function

    Public Function Create(portName As String) As ISerialLink Implements ISerialLinkFactory.Create
        Return New SerialPortLink(portName)
    End Function
End Class

Public Class SerialPortLink
    Implements ISerialLink

    Private ReadOnly _port As SerialPort

    Public Sub New(portName As String)
        ' Wie tools\serial-test.ps1: DTR/RTS aus, sonst kann der ESP32-S3 (USB-Serial-JTAG) neu starten.
        _port = New SerialPort(portName, 115200) With {
            .NewLine = vbLf,
            .Encoding = Text.Encoding.ASCII,
            .ReadTimeout = 50,
            .WriteTimeout = 500,
            .DtrEnable = False,
            .RtsEnable = False}
    End Sub

    Public ReadOnly Property PortName As String Implements ISerialLink.PortName
        Get
            Return _port.PortName
        End Get
    End Property

    Public Sub Open() Implements ISerialLink.Open
        _port.Open()
        _port.DiscardInBuffer()
    End Sub

    Public Sub WriteLine(line As String) Implements ISerialLink.WriteLine
        _port.Write(line & vbLf)
    End Sub

    Public Function ReadLine(timeoutMs As Integer) As String Implements ISerialLink.ReadLine
        If Not _port.IsOpen Then Throw New IO.IOException("Port geschlossen")
        _port.ReadTimeout = Math.Max(1, timeoutMs)
        Try
            Return _port.ReadLine().TrimEnd(ChrW(13))
        Catch ex As TimeoutException
            Return Nothing
        Catch ex As InvalidOperationException
            Throw New IO.IOException(ex.Message, ex)
        Catch ex As UnauthorizedAccessException
            Throw New IO.IOException(ex.Message, ex)   ' USB abgezogen
        End Try
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            If _port.IsOpen Then _port.Close()
        Catch
        End Try
        Try
            _port.Dispose()
        Catch
        End Try
    End Sub
End Class

' ====================================================================== Tastatur (SendInput)

Public Module NativeInput
    Private Const INPUT_KEYBOARD As UInteger = 1
    Private Const KEYEVENTF_EXTENDEDKEY As UInteger = &H1
    Private Const KEYEVENTF_KEYUP As UInteger = &H2
    Private Const KEYEVENTF_UNICODE As UInteger = &H4
    Private Const VK_CONTROL As UShort = &H11
    Private Const VK_RETURN As UShort = &HD

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MOUSEINPUT
        Public dx As Integer
        Public dy As Integer
        Public mouseData As UInteger
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KEYBDINPUT
        Public wVk As UShort
        Public wScan As UShort
        Public dwFlags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    ' MOUSEINPUT ist das größte Element der Union und sorgt für die richtige Strukturgröße.
    <StructLayout(LayoutKind.Explicit)>
    Private Structure InputUnion
        <FieldOffset(0)> Public mi As MOUSEINPUT
        <FieldOffset(0)> Public ki As KEYBDINPUT
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure INPUT
        Public type As UInteger
        Public U As InputUnion
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SendInput(nInputs As UInteger, pInputs As INPUT(), cbSize As Integer) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Function MapVirtualKey(uCode As UInteger, uMapType As UInteger) As UInteger
    End Function

    Private Function Key(vk As UShort, up As Boolean) As INPUT
        Dim flags As UInteger = If(up, KEYEVENTF_KEYUP, 0UI)
        If Hotkeys.IsExtendedKey(vk) Then flags = flags Or KEYEVENTF_EXTENDEDKEY
        Dim i As New INPUT With {.type = INPUT_KEYBOARD}
        i.U.ki = New KEYBDINPUT With {.wVk = vk, .wScan = CUShort(MapVirtualKey(vk, 0) And &HFFFFUI), .dwFlags = flags}
        Return i
    End Function

    Private Function UnicodeChar(c As Char, up As Boolean) As INPUT
        Dim i As New INPUT With {.type = INPUT_KEYBOARD}
        i.U.ki = New KEYBDINPUT With {.wVk = 0, .wScan = CUShort(AscW(c) And &HFFFF), .dwFlags = KEYEVENTF_UNICODE Or If(up, KEYEVENTF_KEYUP, 0UI)}
        Return i
    End Function

    Private Sub Send(inputs As List(Of INPUT))
        If inputs.Count = 0 Then Return
        Dim arr = inputs.ToArray()
        Dim sent = SendInput(CUInt(arr.Length), arr, Marshal.SizeOf(GetType(INPUT)))
        If sent <> arr.Length Then
            Throw New ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),
                "Tastendruck wurde von Windows blockiert (Zielprogramm läuft evtl. als Administrator).")
        End If
    End Sub

    ''' <summary>Alle Tasten in Reihenfolge drücken, dann rückwärts loslassen.</summary>
    Public Sub SendCombo(vks As IList(Of UShort))
        Dim inputs As New List(Of INPUT)
        For Each vk In vks
            inputs.Add(Key(vk, False))
        Next
        For i = vks.Count - 1 To 0 Step -1
            inputs.Add(Key(vks(i), True))
        Next
        Send(inputs)
    End Sub

    ''' <summary>Tippt Text Zeichen für Zeichen (Unicode, unabhängig vom Tastaturlayout).</summary>
    Public Sub TypeText(text As String)
        If String.IsNullOrEmpty(text) Then Return
        Dim inputs As New List(Of INPUT)
        For Each c In text.Replace(vbCrLf, vbLf)
            If c = ChrW(10) Then
                inputs.Add(Key(VK_RETURN, False))
                inputs.Add(Key(VK_RETURN, True))
            Else
                inputs.Add(UnicodeChar(c, False))
                inputs.Add(UnicodeChar(c, True))
            End If
            If inputs.Count >= 200 Then
                Send(inputs)
                inputs.Clear()
            End If
        Next
        Send(inputs)
    End Sub

    ''' <summary>Text über die Zwischenablage einfügen (Strg+V); der vorige Text der Zwischenablage wird wiederhergestellt.</summary>
    Public Sub PasteText(text As String)
        If String.IsNullOrEmpty(text) Then Return
        Dim failure As Exception = Nothing
        Dim t As New Thread(
            Sub()
                Try
                    Dim previous As String = Nothing
                    Try
                        If Clipboard.ContainsText() Then previous = Clipboard.GetText()
                    Catch
                    End Try
                    Clipboard.SetDataObject(text, True, 10, 50)
                    Thread.Sleep(30)
                    SendCombo({VK_CONTROL, CUShort(AscW("V"c))})
                    Thread.Sleep(400)   ' Zielprogramm Zeit zum Lesen geben
                    If previous IsNot Nothing Then Clipboard.SetDataObject(previous, True, 10, 50)
                Catch ex As Exception
                    failure = ex
                End Try
            End Sub)
        t.SetApartmentState(ApartmentState.STA)   ' Zwischenablage braucht STA
        t.IsBackground = True
        t.Start()
        t.Join()
        If failure IsNot Nothing Then Throw New InvalidOperationException("Einfügen fehlgeschlagen: " & failure.Message, failure)
    End Sub

    ''' <summary>Registriert hotkey, media und text beim Runner.</summary>
    Public Sub Register(runner As ActionRunner)
        runner.Register(ActionTypes.Hotkey, DelegateActionHandler.FromSync(Sub(a) SendCombo(Hotkeys.ParseHotkey(a.Keys))))
        runner.Register(ActionTypes.Media, DelegateActionHandler.FromSync(Sub(a) SendCombo({Hotkeys.MediaKeyCode(a.MediaKey)})))
        runner.Register(ActionTypes.Text, DelegateActionHandler.FromSync(
            Sub(a)
                If a.Paste.GetValueOrDefault() Then PasteText(a.Text) Else TypeText(a.Text)
                If a.Enter.GetValueOrDefault() Then SendCombo({VK_RETURN})
            End Sub))
    End Sub
End Module

' ====================================================================== Autostart

Public Module Autostart
    Private Const RunKey As String = "Software\Microsoft\Windows\CurrentVersion\Run"
    Private Const ValueName As String = "DIY StreamDeck"

    Private ReadOnly Property Command As String
        Get
            Return $"""{Environment.ProcessPath}"" --tray"
        End Get
    End Property

    Public Function IsEnabled() As Boolean
        Using k = Registry.CurrentUser.OpenSubKey(RunKey, False)
            Return k IsNot Nothing AndAlso k.GetValue(ValueName) IsNot Nothing
        End Using
    End Function

    Public Sub SetEnabled(enabled As Boolean)
        Using k = Registry.CurrentUser.CreateSubKey(RunKey, True)
            If enabled Then
                k.SetValue(ValueName, Command)
            ElseIf k.GetValue(ValueName) IsNot Nothing Then
                k.DeleteValue(ValueName, False)
            End If
        End Using
    End Sub

    ''' <summary>Falls die EXE verschoben wurde: eingetragenen Pfad aktualisieren.</summary>
    Public Sub RefreshPathIfEnabled()
        Try
            Using k = Registry.CurrentUser.OpenSubKey(RunKey, True)
                Dim v = TryCast(k?.GetValue(ValueName), String)
                If v IsNot Nothing AndAlso v <> Command Then k.SetValue(ValueName, Command)
            End Using
        Catch
        End Try
    End Sub
End Module

' ====================================================================== Vordergrundprogramm

Public Module ForegroundProcess
    <DllImport("user32.dll")>
    Private Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    ''' <summary>Prozessname des aktiven Fensters (z. B. "obs64") oder Nothing.</summary>
    Public Function GetName() As String
        Try
            Dim hwnd = GetForegroundWindow()
            If hwnd = IntPtr.Zero Then Return Nothing
            Dim pid As UInteger
            GetWindowThreadProcessId(hwnd, pid)
            If pid = 0 Then Return Nothing
            Using p = Process.GetProcessById(CInt(pid))
                Return p.ProcessName
            End Using
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Namen aller Programme mit sichtbarem Fenster (für die Auswahl im Profil).</summary>
    Public Function GetWindowedProcessNames() As List(Of String)
        Dim names As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each p In Process.GetProcesses()
            Try
                If p.MainWindowHandle <> IntPtr.Zero AndAlso Not String.IsNullOrEmpty(p.MainWindowTitle) Then names.Add(p.ProcessName)
            Catch
            Finally
                p.Dispose()
            End Try
        Next
        Return names.ToList()
    End Function
End Module
