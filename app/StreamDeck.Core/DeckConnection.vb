' Automatische Geräteerkennung und Verbindung zum Stream Deck über eine serielle Schnittstelle.
'
' Ablauf (eigener Hintergrund-Thread):
'   1. Alle Ports parallel öffnen, "HELLO?" senden, auf "HELLO STREAMDECK …" warten.
'   2. Mit dem gefundenen Port: jede Sekunde "PING", Zeilen auswerten, D/U als Ereignis melden.
'   3. Kommt länger als DeadTimeoutMs nichts mehr (oder der Port verschwindet), Verbindung
'      trennen und wieder bei 1. beginnen. So klappt Ab- und Anstecken ohne Zutun.
'
' Die eigentliche Schnittstelle steckt hinter ISerialLink, damit sich alles ohne Hardware testen lässt.
Imports System.Threading

''' <summary>Eine geöffnete serielle Verbindung (zeilenbasiert).</summary>
Public Interface ISerialLink
    Inherits IDisposable
    ReadOnly Property PortName As String
    Sub Open()
    Sub WriteLine(line As String)
    ''' <summary>Liefert die nächste Zeile ohne Zeilenende oder Nothing nach timeoutMs. Wirft IOException, wenn der Port weg ist.</summary>
    Function ReadLine(timeoutMs As Integer) As String
End Interface

Public Interface ISerialLinkFactory
    Function GetPortNames() As String()
    Function Create(portName As String) As ISerialLink
End Interface

Public Class DeckConnection
    Implements IDisposable

    Private ReadOnly _factory As ISerialLinkFactory
    Private ReadOnly _writeLock As New Object
    Private _thread As Thread
    Private _cts As CancellationTokenSource
    Private _link As ISerialLink
    Private _lastPort As String

    ''' <summary>Wartezeit auf die HELLO-Antwort je Port.</summary>
    Public Property HelloTimeoutMs As Integer = 1500
    Public Property PingIntervalMs As Integer = 1000
    ''' <summary>Ohne jede Zeile vom Gerät gilt die Verbindung danach als verloren.</summary>
    Public Property DeadTimeoutMs As Integer = 3500
    ''' <summary>Pause zwischen zwei Suchläufen, solange kein Gerät gefunden wurde.</summary>
    Public Property RescanDelayMs As Integer = 2000
    Public Property ReadSliceMs As Integer = 50
    ''' <summary>Ports ohne Deck werden erst nach dieser Zeit wieder abgefragt.</summary>
    Public Property FailedPortRetryMs As Integer = 15000

    Private ReadOnly _backoff As New Dictionary(Of String, Long)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _knownPorts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Public Event Connected(portName As String)
    Public Event Disconnected()
    Public Event KeyDown(keyIndex As Integer)
    Public Event KeyUp(keyIndex As Integer)
    Public Event Log(message As String)

    Public Sub New(factory As ISerialLinkFactory)
        _factory = factory
    End Sub

    Public ReadOnly Property IsConnected As Boolean
        Get
            Return Volatile.Read(_link) IsNot Nothing
        End Get
    End Property

    Public ReadOnly Property PortName As String
        Get
            Return Volatile.Read(_link)?.PortName
        End Get
    End Property

    ''' <summary>Anzahl Tasten laut HELLO des Geräts.</summary>
    Public ReadOnly Property KeyCount As Integer

    Public Sub Start()
        If _thread IsNot Nothing Then Return
        _cts = New CancellationTokenSource()
        _thread = New Thread(AddressOf Run) With {.IsBackground = True, .Name = "DeckConnection"}
        _thread.Start(_cts.Token)
    End Sub

    Public Sub [Stop]()
        If _thread Is Nothing Then Return
        _cts.Cancel()
        _thread.Join(5000)
        _thread = Nothing
        _cts.Dispose()
        _cts = Nothing
    End Sub

    ''' <summary>Sendet eine Zeile an das Gerät (z. B. "LED 0 40 0"). Ohne Verbindung wird nichts gesendet.</summary>
    Public Function Send(line As String) As Boolean
        Dim link = Volatile.Read(_link)
        If link Is Nothing Then Return False
        Try
            SyncLock _writeLock
                link.WriteLine(line)
            End SyncLock
            Return True
        Catch ex As Exception
            RaiseEvent Log($"Senden fehlgeschlagen: {ex.Message}")
            Return False
        End Try
    End Function

    Private Sub Run(state As Object)
        Dim token = DirectCast(state, CancellationToken)
        Dim announcedSearch = False
        While Not token.IsCancellationRequested
            Dim link As ISerialLink = Nothing
            Try
                If Not announcedSearch Then
                    RaiseEvent Log("Suche Stream Deck …")
                    announcedSearch = True
                End If
                link = Discover(token)
            Catch ex As Exception
                RaiseEvent Log($"Fehler bei der Gerätesuche: {ex.Message}")
            End Try

            If link Is Nothing Then
                token.WaitHandle.WaitOne(RescanDelayMs)
                Continue While
            End If

            announcedSearch = False
            _lastPort = link.PortName
            Volatile.Write(_link, link)
            RaiseEvent Log($"Verbunden mit {link.PortName}")
            RaiseEvent Connected(link.PortName)
            Try
                RunSession(link, token)
            Catch ex As Exception
                If Not token.IsCancellationRequested Then RaiseEvent Log($"Verbindung zu {link.PortName} verloren: {ex.Message}")
            Finally
                Volatile.Write(_link, Nothing)
                SafeDispose(link)
                RaiseEvent Disconnected()
            End Try
        End While
    End Sub

    ''' <summary>
    ''' Fragt alle Ports parallel ab und nimmt das erste Deck, das antwortet – ohne auf langsame Ports
    ''' (z. B. Bluetooth-COM-Ports, deren Öffnen Sekunden dauert) zu warten. Ports ohne Deck werden erst
    ''' nach FailedPortRetryMs wieder gefragt; neu aufgetauchte Ports (Gerät eingesteckt) sofort.
    ''' </summary>
    Private Function Discover(token As CancellationToken) As ISerialLink
        Dim now = Environment.TickCount64
        Dim ports = _factory.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        SyncLock _backoff
            For Each p In ports
                If Not _knownPorts.Contains(p) Then _backoff.Remove(p)   ' neu aufgetaucht -> sofort fragen
            Next
            _knownPorts.Clear()
            _knownPorts.UnionWith(ports)
            ports = ports.Where(Function(p)
                                    Dim due As Long
                                    Return Not _backoff.TryGetValue(p, due) OrElse now >= due
                                End Function).ToList()
        End SyncLock
        If ports.Count = 0 Then Return Nothing
        If _lastPort IsNot Nothing AndAlso ports.Remove(_lastPort) Then ports.Insert(0, _lastPort)

        Dim winner As New TaskCompletionSource(Of ISerialLink)(TaskCreationOptions.RunContinuationsAsynchronously)
        Dim tasks = ports.Select(Function(p) Task.Run(
            Sub()
                Dim link = Probe(p, token)
                SyncLock _backoff
                    If link Is Nothing Then _backoff(p) = Environment.TickCount64 + FailedPortRetryMs Else _backoff.Remove(p)
                End SyncLock
                If link IsNot Nothing AndAlso Not winner.TrySetResult(link) Then SafeDispose(link)   ' nur ein Gerät wird benutzt
            End Sub)).ToArray()
        Dim all = Task.WhenAll(tasks).ContinueWith(Sub(t) winner.TrySetResult(Nothing), TaskScheduler.Default)
        Try
            winner.Task.Wait(token)
        Catch ex As OperationCanceledException
            Return Nothing
        End Try
        Return winner.Task.Result
    End Function

    Private Function Probe(portName As String, token As CancellationToken) As ISerialLink
        Dim link As ISerialLink = Nothing
        Try
            link = _factory.Create(portName)
            link.Open()
            link.WriteLine(DeviceProtocol.HelloRequest)
            Dim deadline = Environment.TickCount64 + HelloTimeoutMs
            While Environment.TickCount64 < deadline AndAlso Not token.IsCancellationRequested
                Dim line = link.ReadLine(ReadSliceMs)
                If line Is Nothing Then Continue While
                Dim msg = DeviceProtocol.Parse(line)
                If msg.Kind = MessageKind.Hello Then
                    If msg.ProtocolVersion <> DeviceProtocol.SupportedVersion Then
                        RaiseEvent Log($"{portName}: Protokollversion {msg.ProtocolVersion} wird nicht unterstützt (erwartet {DeviceProtocol.SupportedVersion}).")
                        Exit While
                    End If
                    _KeyCount = msg.KeyCount
                    Return link
                End If
            End While
        Catch
            ' Port belegt, verschwunden oder kein passendes Gerät – einfach weiter
        End Try
        SafeDispose(link)
        Return Nothing
    End Function

    Private Sub RunSession(link As ISerialLink, token As CancellationToken)
        Dim lastRx = Environment.TickCount64
        Dim nextPing = Environment.TickCount64
        While Not token.IsCancellationRequested
            Dim now = Environment.TickCount64
            If now >= nextPing Then
                SyncLock _writeLock
                    link.WriteLine(DeviceProtocol.PingRequest)
                End SyncLock
                nextPing = now + PingIntervalMs
            End If

            Dim line = link.ReadLine(ReadSliceMs)
            now = Environment.TickCount64
            If line Is Nothing Then
                If now - lastRx > DeadTimeoutMs Then Throw New IO.IOException($"seit {DeadTimeoutMs} ms keine Antwort")
                Continue While
            End If

            lastRx = now
            Dim msg = DeviceProtocol.Parse(line)
            Select Case msg.Kind
                Case MessageKind.KeyDown : RaiseEvent KeyDown(msg.KeyIndex)
                Case MessageKind.KeyUp : RaiseEvent KeyUp(msg.KeyIndex)
                Case MessageKind.Hello : RaiseEvent Log("Gerät hat sich neu gemeldet (Neustart?).")
            End Select
        End While
    End Sub

    Private Shared Sub SafeDispose(link As ISerialLink)
        Try
            link?.Dispose()
        Catch
        End Try
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        [Stop]()
    End Sub
End Class
