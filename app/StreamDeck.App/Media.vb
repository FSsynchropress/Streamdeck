' Aktionstypen sound (NAudio) und obs (obs-websocket-dotnet, OBS WebSocket 5.x).
Imports System.Threading
Imports NAudio.CoreAudioApi
Imports NAudio.Wave
Imports OBSWebsocketDotNet
Imports OBSWebsocketDotNet.Communication
Imports StreamDeck.Core

' ====================================================================== Sound

''' <summary>Spielt WAV/MP3 ab. Erneuter Druck stoppt den laufenden Sound (mit restart: startet ihn neu).</summary>
Public Class SoundActionHandler
    Implements IActionHandler, IDisposable

    Private ReadOnly _lock As New Object
    Private ReadOnly _playing As New Dictionary(Of String, IWavePlayer)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _log As Action(Of String)

    Public Sub New(log As Action(Of String))
        _log = log
    End Sub

    Public Const BuiltInFolder As String = "Sounds"
    ''' <summary>Ausblendzeit am Ende, wenn eine Dauer gesetzt ist.</summary>
    Public Const FadeOutSeconds As Double = 0.2

    ''' <summary>Mitgelieferte Sounds als relative Pfade ("Sounds\tusch.mp3"), alphabetisch.</summary>
    Public Shared Function GetBuiltInSounds() As List(Of String)
        Dim dir = IO.Path.Combine(AppContext.BaseDirectory, BuiltInFolder)
        If Not IO.Directory.Exists(dir) Then Return New List(Of String)
        Return IO.Directory.GetFiles(dir).
            Where(Function(f) {".mp3", ".wav"}.Contains(IO.Path.GetExtension(f).ToLowerInvariant())).
            Select(Function(f) IO.Path.Combine(BuiltInFolder, IO.Path.GetFileName(f))).
            OrderBy(Function(f) f, StringComparer.CurrentCultureIgnoreCase).ToList()
    End Function

    ''' <summary>Namen der aktiven Ausgabegeräte (für die Auswahl im Editor).</summary>
    Public Shared Function GetOutputDevices() As List(Of String)
        Dim result As New List(Of String)
        Try
            Using en As New MMDeviceEnumerator()
                For Each d In en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    result.Add(d.FriendlyName)
                    d.Dispose()
                Next
            End Using
        Catch
        End Try
        Return result
    End Function

    Private Function FindDevice(name As String) As MMDevice
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Using en As New MMDeviceEnumerator()
            Dim all = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList()
            Dim hit = If(all.FirstOrDefault(Function(d) d.FriendlyName.Equals(name, StringComparison.OrdinalIgnoreCase)),
                         all.FirstOrDefault(Function(d) d.FriendlyName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0))
            For Each d In all
                If d IsNot hit Then d.Dispose()
            Next
            If hit Is Nothing Then _log($"Ausgabegerät '{name}' nicht gefunden – Standardgerät wird benutzt.")
            Return hit
        End Using
    End Function

    Public Function ExecuteAsync(a As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        If String.IsNullOrWhiteSpace(a.File) Then Throw New ArgumentException("Keine Sounddatei angegeben.")
        Dim file = ProcessActions.ResolveAppRelativePath(a.File, AppContext.BaseDirectory)

        Dim running As IWavePlayer = Nothing
        SyncLock _lock
            If _playing.TryGetValue(file, running) Then _playing.Remove(file)
        End SyncLock
        If running IsNot Nothing Then
            ' Stop() NICHT unter _lock aufrufen: NAudio wartet dabei auf den
            ' Wiedergabe-Thread, und dessen PlaybackStopped-Handler braucht _lock -> Deadlock.
            running.Stop()
            ' Erneuter Druck stoppt – außer "restart" ist gesetzt, dann von vorn starten.
            If Not a.Restart.GetValueOrDefault() Then Return Task.CompletedTask
        End If

        If Not IO.File.Exists(file) Then Throw New IO.FileNotFoundException($"Datei nicht gefunden: {file}")

        Dim reader As New AudioFileReader(file) With {.Volume = Math.Max(0, Math.Min(100, a.Volume.GetValueOrDefault(100))) / 100.0F}
        ' Optional nach "duration" Sekunden enden – am Ende kurz ausblenden, damit es nicht knackt.
        Dim source As ISampleProvider = reader
        Dim seconds = a.Duration.GetValueOrDefault()
        If seconds > 0 AndAlso TimeSpan.FromSeconds(seconds) < reader.TotalTime Then source = New TrimFadeSampleProvider(reader, seconds, FadeOutSeconds)
        Dim device = FindDevice(a.Device)
        Dim output As IWavePlayer
        Try
            output = If(device Is Nothing,
                        New WasapiOut(AudioClientShareMode.Shared, 80),
                        New WasapiOut(device, AudioClientShareMode.Shared, True, 80))
            output.Init(source)
        Catch
            reader.Dispose()
            Throw
        End Try

        AddHandler output.PlaybackStopped,
            Sub(s, e)
                SyncLock _lock
                    Dim current As IWavePlayer = Nothing
                    If _playing.TryGetValue(file, current) AndAlso current Is output Then _playing.Remove(file)
                End SyncLock
                If e.Exception IsNot Nothing Then _log($"Wiedergabe-Fehler: {e.Exception.Message}")
                ' nicht im Wiedergabe-Thread selbst aufräumen
                Task.Run(Sub()
                             output.Dispose()
                             reader.Dispose()
                             device?.Dispose()
                         End Sub)
            End Sub

        SyncLock _lock
            _playing(file) = output
        End SyncLock
        output.Play()
        Return Task.CompletedTask
    End Function

    Public Sub StopAll()
        Dim all As List(Of IWavePlayer)
        SyncLock _lock
            all = _playing.Values.ToList()
            _playing.Clear()
        End SyncLock
        For Each p In all
            Try
                p.Stop()
            Catch
            End Try
        Next
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        StopAll()
    End Sub
End Class

''' <summary>
''' Liefert nur die ersten <c>seconds</c> Sekunden einer Quelle und blendet die letzten
''' <c>fadeSeconds</c> davon linear aus. Danach ist die Quelle zu Ende (Wiedergabe stoppt von selbst).
''' </summary>
Public Class TrimFadeSampleProvider
    Implements ISampleProvider

    Private ReadOnly _source As ISampleProvider
    Private ReadOnly _total As Long        ' Samples insgesamt (alle Kanäle)
    Private ReadOnly _fadeStart As Long
    Private _position As Long

    Public Sub New(source As ISampleProvider, seconds As Double, fadeSeconds As Double)
        _source = source
        _total = CLng(Math.Round(seconds * source.WaveFormat.SampleRate)) * source.WaveFormat.Channels
        Dim fade = Math.Min(_total, CLng(Math.Round(Math.Min(fadeSeconds, seconds / 2) * source.WaveFormat.SampleRate)) * source.WaveFormat.Channels)
        _fadeStart = _total - fade
    End Sub

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _source.WaveFormat
        End Get
    End Property

    Public Function Read(buffer As Single(), offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Dim remaining = _total - _position
        If remaining <= 0 Then Return 0
        Dim n = _source.Read(buffer, offset, CInt(Math.Min(count, remaining)))
        Dim fadeLen = _total - _fadeStart
        If fadeLen > 0 Then
            For i = 0 To n - 1
                Dim pos = _position + i
                If pos >= _fadeStart Then buffer(offset + i) *= CSng((_total - pos) / fadeLen)
            Next
        End If
        _position += n
        Return n
    End Function
End Class

' ====================================================================== Mikrofon

''' <summary>
''' Schaltet Mikrofone systemweit stumm/an (Windows-Lautstärke der Aufnahmegeräte, wirkt in allen Programmen).
''' Ohne Gerätename: alle aktiven Aufnahmegeräte; der neue Zustand richtet sich beim Umschalten nach dem
''' Standard-Aufnahmegerät, damit alle Geräte danach gleich stehen.
''' </summary>
Public Class MicActionHandler
    Implements IActionHandler

    Private ReadOnly _runner As ActionRunner
    Private ReadOnly _log As Action(Of String)

    Public Sub New(runner As ActionRunner, log As Action(Of String))
        _runner = runner
        _log = log
    End Sub

    ''' <summary>Namen der aktiven Aufnahmegeräte (für die Auswahl im Editor).</summary>
    Public Shared Function GetInputDevices() As List(Of String)
        Dim result As New List(Of String)
        Try
            Using en As New MMDeviceEnumerator()
                For Each d In en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    result.Add(d.FriendlyName)
                    d.Dispose()
                Next
            End Using
        Catch
        End Try
        Return result
    End Function

    Public Function ExecuteAsync(a As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Dim muted = SetMute(If(a.Op, "toggle"), a.Device)
        Dim text = If(muted, "Mikrofon stumm", "Mikrofon an")
        _log(text)
        _runner.RaiseNotify(text, If(String.IsNullOrWhiteSpace(a.Device), "alle Aufnahmegeräte", a.Device))
        Return Task.CompletedTask
    End Function

    ''' <summary>Setzt den Zustand und liefert, ob jetzt stumm ist.</summary>
    Public Shared Function SetMute(op As String, deviceName As String) As Boolean
        Using en As New MMDeviceEnumerator()
            Dim all = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList()
            Try
                If all.Count = 0 Then Throw New InvalidOperationException("Kein aktives Mikrofon gefunden.")
                Dim targets = all
                If Not String.IsNullOrWhiteSpace(deviceName) Then
                    Dim name = deviceName.Trim()
                    targets = all.Where(Function(d) d.FriendlyName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList()
                    If targets.Count = 0 Then targets = all.Where(Function(d) d.FriendlyName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
                    If targets.Count = 0 Then Throw New InvalidOperationException($"Aufnahmegerät '{deviceName}' nicht gefunden.")
                End If

                Dim mute As Boolean
                Select Case op.Trim().ToLowerInvariant()
                    Case "mute" : mute = True
                    Case "unmute" : mute = False
                    Case "toggle", ""
                        Dim reference = targets(0)
                        If targets.Count > 1 Then
                            Try
                                Dim defId = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID
                                reference = If(targets.FirstOrDefault(Function(d) d.ID = defId), targets(0))
                            Catch
                            End Try
                        End If
                        mute = Not reference.AudioEndpointVolume.Mute
                    Case Else
                        Throw New ArgumentException($"Unbekannte Mikrofon-Aktion '{op}'.")
                End Select

                For Each d In targets
                    d.AudioEndpointVolume.Mute = mute
                Next
                Return mute
            Finally
                For Each d In all
                    d.Dispose()
                Next
            End Try
        End Using
    End Function
End Class

' ====================================================================== OBS

''' <summary>Steuert OBS über den eingebauten WebSocket-Server (OBS ≥ 28, Extras → WebSocket-Servereinstellungen).</summary>
Public Class ObsActionHandler
    Implements IActionHandler, IDisposable

    Private ReadOnly _obs As New OBSWebsocket()
    Private ReadOnly _getSettings As Func(Of ObsSettings)
    Private ReadOnly _connectLock As New SemaphoreSlim(1, 1)

    Public Sub New(getSettings As Func(Of ObsSettings))
        _getSettings = getSettings
    End Sub

    Private Async Function EnsureConnectedAsync() As Task
        If _obs.IsConnected Then Return
        Await _connectLock.WaitAsync().ConfigureAwait(False)
        Try
            If _obs.IsConnected Then Return
            Dim s = _getSettings()
            Dim url = If(String.IsNullOrWhiteSpace(s?.Url), "ws://localhost:4455", s.Url.Trim())
            Dim tcs As New TaskCompletionSource(Of Boolean)(TaskCreationOptions.RunContinuationsAsynchronously)
            Dim onConnected As EventHandler = Sub(o, e) tcs.TrySetResult(True)
            Dim onDisconnected As EventHandler(Of ObsDisconnectionInfo) =
                Sub(o, e)
                    Dim reason = e?.DisconnectReason
                    tcs.TrySetException(New InvalidOperationException(
                        "OBS-Verbindung abgelehnt" & If(String.IsNullOrEmpty(reason), " (Passwort prüfen)", ": " & reason)))
                End Sub
            AddHandler _obs.Connected, onConnected
            AddHandler _obs.Disconnected, onDisconnected
            Try
                _obs.ConnectAsync(url, If(s?.Password, ""))
                Dim finished = Await Task.WhenAny(tcs.Task, Task.Delay(4000)).ConfigureAwait(False)
                If finished IsNot tcs.Task Then
                    Try
                        _obs.Disconnect()
                    Catch
                    End Try
                    Throw New TimeoutException($"OBS antwortet nicht unter {url} – läuft OBS und ist der WebSocket-Server aktiviert?")
                End If
                Await tcs.Task.ConfigureAwait(False)
            Finally
                RemoveHandler _obs.Connected, onConnected
                RemoveHandler _obs.Disconnected, onDisconnected
            End Try
        Finally
            _connectLock.Release()
        End Try
    End Function

    ''' <summary>Nach Änderung der Einstellungen: Verbindung trennen, beim nächsten Druck neu verbinden.</summary>
    Public Sub Reset()
        Try
            If _obs.IsConnected Then _obs.Disconnect()
        Catch
        End Try
    End Sub

    Public Async Function ExecuteAsync(a As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Await EnsureConnectedAsync().ConfigureAwait(False)
        Dim op = If(a.Op, "").Trim()
        Select Case op.ToLowerInvariant()
            Case "scene"
                If String.IsNullOrWhiteSpace(a.Scene) Then Throw New ArgumentException("Keine Szene angegeben.")
                _obs.SetCurrentProgramScene(a.Scene.Trim())
            Case "startrecord" : _obs.StartRecord()
            Case "stoprecord" : _obs.StopRecord()
            Case "togglerecord" : _obs.ToggleRecord()
            Case "startstream" : _obs.StartStream()
            Case "stopstream" : _obs.StopStream()
            Case "togglestream" : _obs.ToggleStream()
            Case "showsource", "hidesource", "togglesource"
                If String.IsNullOrWhiteSpace(a.Source) Then Throw New ArgumentException("Keine Quelle angegeben.")
                Dim scene = If(String.IsNullOrWhiteSpace(a.Scene), _obs.GetCurrentProgramScene(), a.Scene.Trim())
                Dim itemId = _obs.GetSceneItemId(scene, a.Source.Trim(), 0)
                Dim visible As Boolean
                Select Case op.ToLowerInvariant()
                    Case "showsource" : visible = True
                    Case "hidesource" : visible = False
                    Case Else : visible = Not _obs.GetSceneItemEnabled(scene, itemId)
                End Select
                _obs.SetSceneItemEnabled(scene, itemId, visible)
            Case Else
                Throw New ArgumentException($"Unbekannte OBS-Aktion '{a.Op}'.")
        End Select
    End Function

    Public Sub Dispose() Implements IDisposable.Dispose
        Reset()
    End Sub
End Class
