' Sprach-Mix: Mikrofon + Sounds auf ein virtuelles Audiokabel, damit Discord & Co. die Sounds hören.
'
' Discord kann nur ein Eingabegerät benutzen. Die App nimmt deshalb das echte Mikrofon auf, mischt die
' Sounds dazu und spielt das Ergebnis auf dem Eingang eines virtuellen Kabels ab (z. B. "CABLE Input" von
' VB-Audio Virtual Cable). In Discord wählt man als Eingabegerät den Ausgang des Kabels ("CABLE Output").
Imports NAudio.CoreAudioApi
Imports NAudio.Wave
Imports NAudio.Wave.SampleProviders
Imports StreamDeck.Core

Public Class VoiceMixer
    Implements IDisposable

    ''' <summary>Mehr Mikrofon-Puffer wird verworfen, damit die Verzögerung nicht wächst (Taktabweichung der Geräte).</summary>
    Private Const MaxMicBufferMs As Integer = 150

    Private ReadOnly _log As Action(Of String)
    Private ReadOnly _lock As New Object
    Private _capture As WasapiCapture
    Private _output As WasapiOut
    Private _mixer As MixingSampleProvider
    Private _devices As New List(Of MMDevice)

    Public Sub New(log As Action(Of String))
        _log = log
    End Sub

    ''' <summary>Läuft der Mix gerade (Kabel gefunden, Ausgabe gestartet)?</summary>
    Public ReadOnly Property IsRunning As Boolean
        Get
            SyncLock _lock
                Return _mixer IsNot Nothing
            End SyncLock
        End Get
    End Property

    ''' <summary>Name des passenden Kabel-Ausgabegeräts oder Nothing.</summary>
    Public Shared Function FindCableOutput(preferred As String) As String
        Dim names = SoundActionHandler.GetOutputDevices()
        If Not String.IsNullOrWhiteSpace(preferred) Then
            Dim hit = names.FirstOrDefault(Function(n) n.IndexOf(preferred.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
            If hit IsNot Nothing Then Return hit
        End If
        Return names.FirstOrDefault(Function(n) n.IndexOf("CABLE Input", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                                                 n.IndexOf("VB-Audio", StringComparison.OrdinalIgnoreCase) >= 0)
    End Function

    ''' <summary>Startet/stoppt den Mix passend zu den Einstellungen. Liefert eine Fehlermeldung oder Nothing.</summary>
    Public Function Apply(settings As VoiceMixSettings) As String
        StopMix()
        If settings Is Nothing OrElse Not settings.Enabled Then Return Nothing
        Try
            StartMix(settings)
            Return Nothing
        Catch ex As Exception
            StopMix()
            _log($"Sprach-Mix: {ex.Message}")
            Return ex.Message
        End Try
    End Function

    Private Sub StartMix(s As VoiceMixSettings)
        Using en As New MMDeviceEnumerator()
            Dim render = FindDevice(en, DataFlow.Render, s.Output)
            If render Is Nothing Then
                Throw New InvalidOperationException($"Ausgabegerät '{s.Output}' nicht gefunden – ist VB-Audio Virtual Cable installiert?")
            End If
            _devices.Add(render)

            Dim mic As MMDevice
            If String.IsNullOrWhiteSpace(s.Microphone) Then
                mic = en.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)
            Else
                mic = FindDevice(en, DataFlow.Capture, s.Microphone)
                If mic Is Nothing Then Throw New InvalidOperationException($"Mikrofon '{s.Microphone}' nicht gefunden.")
            End If
            _devices.Add(mic)
            If mic.FriendlyName.IndexOf("CABLE", StringComparison.OrdinalIgnoreCase) >= 0 Then
                Throw New InvalidOperationException("Als Mikrofon ist das virtuelle Kabel eingestellt – bitte das echte Mikrofon auswählen.")
            End If

            Dim format = WaveFormat.CreateIeeeFloatWaveFormat(render.AudioClient.MixFormat.SampleRate, 2)
            Dim mixer As New MixingSampleProvider(format) With {.ReadFully = True}

            ' Mikrofon
            Dim capture As New WasapiCapture(mic, True, 20)
            Dim buffer As New BufferedWaveProvider(capture.WaveFormat) With {
                .DiscardOnBufferOverflow = True, .BufferDuration = TimeSpan.FromMilliseconds(500), .ReadFully = True}
            AddHandler capture.DataAvailable,
                Sub(o, e)
                    If buffer.BufferedDuration.TotalMilliseconds > MaxMicBufferMs Then buffer.ClearBuffer()
                    buffer.AddSamples(e.Buffer, 0, e.BytesRecorded)
                End Sub
            AddHandler capture.RecordingStopped,
                Sub(o, e)
                    If e.Exception IsNot Nothing Then _log($"Sprach-Mix: Mikrofon-Fehler: {e.Exception.Message}")
                End Sub
            mixer.AddMixerInput(New VolumeSampleProvider(Adapt(buffer.ToSampleProvider(), format)) With {.Volume = s.MicVolume / 100.0F})

            ' Ausgabe aufs Kabel
            Dim output As New WasapiOut(render, AudioClientShareMode.Shared, True, 40)
            output.Init(mixer)
            AddHandler output.PlaybackStopped,
                Sub(o, e)
                    If e.Exception IsNot Nothing Then _log($"Sprach-Mix: Ausgabe-Fehler: {e.Exception.Message}")
                End Sub

            SyncLock _lock
                _capture = capture
                _output = output
                _mixer = mixer
            End SyncLock
            output.Play()
            capture.StartRecording()
            _log($"Sprach-Mix aktiv: {mic.FriendlyName} + Sounds → {render.FriendlyName}")
        End Using
    End Sub

    Private Shared Function FindDevice(en As MMDeviceEnumerator, flow As DataFlow, name As String) As MMDevice
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        Dim all = en.EnumerateAudioEndPoints(flow, DeviceState.Active).ToList()
        Dim n = name.Trim()
        Dim hit = If(all.FirstOrDefault(Function(d) d.FriendlyName.Equals(n, StringComparison.OrdinalIgnoreCase)),
                     all.FirstOrDefault(Function(d) d.FriendlyName.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0))
        For Each d In all
            If d IsNot hit Then d.Dispose()
        Next
        Return hit
    End Function

    ''' <summary>Bringt eine Quelle auf das Format des Mixers (Stereo, gleiche Abtastrate).</summary>
    Public Shared Function Adapt(source As ISampleProvider, format As WaveFormat) As ISampleProvider
        Dim result = source
        ' Mono wird auf beide Kanäle verteilt, mehr als zwei Kanäle auf die ersten beiden reduziert.
        If result.WaveFormat.Channels <> 2 Then result = New MultiplexingSampleProvider({result}, 2)
        If result.WaveFormat.SampleRate <> format.SampleRate Then result = New WdlResamplingSampleProvider(result, format.SampleRate)
        Return result
    End Function

    ''' <summary>
    ''' Mischt eine Quelle in den Mix. Liefert einen Griff zum vorzeitigen Stoppen oder Nothing, wenn der Mix
    ''' nicht läuft. <paramref name="onEnd"/> läuft auf dem Mix-Thread, sobald die Quelle fertig ist.
    ''' </summary>
    Public Function AddSound(source As ISampleProvider, onEnd As Action) As VoiceInput
        Dim mixer As MixingSampleProvider
        SyncLock _lock
            mixer = _mixer
        End SyncLock
        If mixer Is Nothing Then Return Nothing
        Dim input As New VoiceInput(Adapt(source, mixer.WaveFormat), onEnd)
        mixer.AddMixerInput(input)
        Return input
    End Function

    Private Sub StopMix()
        Dim capture As WasapiCapture, output As WasapiOut
        SyncLock _lock
            capture = _capture : output = _output
            _capture = Nothing : _output = Nothing : _mixer = Nothing
        End SyncLock
        Try
            capture?.StopRecording()
        Catch
        End Try
        Try
            output?.Stop()
        Catch
        End Try
        capture?.Dispose()
        output?.Dispose()
        For Each d In _devices
            d.Dispose()
        Next
        _devices = New List(Of MMDevice)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        StopMix()
    End Sub
End Class

''' <summary>Ein Sound im Sprach-Mix. Endet, wenn die Quelle fertig ist oder Stop aufgerufen wurde.</summary>
Public Class VoiceInput
    Implements ISampleProvider

    Private ReadOnly _source As ISampleProvider
    Private _onEnd As Action
    Private _stopped As Boolean

    Public Sub New(source As ISampleProvider, onEnd As Action)
        _source = source
        _onEnd = onEnd
    End Sub

    Public ReadOnly Property WaveFormat As WaveFormat Implements ISampleProvider.WaveFormat
        Get
            Return _source.WaveFormat
        End Get
    End Property

    Public Sub [Stop]()
        _stopped = True
    End Sub

    Public Function Read(buffer As Single(), offset As Integer, count As Integer) As Integer Implements ISampleProvider.Read
        Dim n = 0
        If Not _stopped Then
            Try
                n = _source.Read(buffer, offset, count)
            Catch
                n = 0
            End Try
        End If
        ' Weniger als angefordert = fertig; der Mixer entfernt die Quelle dann von selbst.
        If n < count Then Finish()
        Return n
    End Function

    Private Sub Finish()
        Dim cb = Threading.Interlocked.Exchange(_onEnd, Nothing)
        cb?.Invoke()
    End Sub
End Class
