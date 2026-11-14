' Eigene Sounds aufnehmen: Nachbearbeitung (ohne NAudio, damit testbar) und Ablage der Dateien.
Imports System.IO
Imports System.Text

Public NotInheritable Class RecordingTools
    ''' <summary>Ordner für eigene Aufnahmen: %APPDATA%\StreamDeck\sounds.</summary>
    Public Shared ReadOnly Property Folder As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamDeck", "sounds")
        End Get
    End Property

    ''' <summary>Pfad für die Konfiguration: im Aufnahmeordner mit %APPDATA% (rechnerunabhängig), sonst unverändert.</summary>
    Public Shared Function ConfigPath(fullPath As String, Optional appData As String = Nothing) As String
        Dim root = If(appData, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)).TrimEnd("\"c)
        If fullPath.StartsWith(root & "\", StringComparison.OrdinalIgnoreCase) Then
            Return "%APPDATA%" & fullPath.Substring(root.Length)
        End If
        Return fullPath
    End Function

    ''' <summary>Macht aus einem frei eingegebenen Namen einen gültigen Dateinamen (ohne Endung).</summary>
    Public Shared Function SafeFileName(name As String) As String
        Dim invalid = Path.GetInvalidFileNameChars()
        Dim sb As New StringBuilder()
        For Each ch In If(name, "").Trim()
            sb.Append(If(invalid.Contains(ch), "_"c, ch))
        Next
        Dim s = sb.ToString().Trim().TrimEnd("."c, " "c)
        If s.Length > 60 Then s = s.Substring(0, 60).Trim()
        Return If(s.Length = 0, "Aufnahme", s)
    End Function

    ''' <summary>Freier Dateipfad im Ordner: „Name.wav“, sonst „Name 2.wav“, „Name 3.wav“ …</summary>
    Public Shared Function UniquePath(folder As String, name As String, extension As String) As String
        Dim baseName = SafeFileName(name)
        Dim candidate = Path.Combine(folder, baseName & extension)
        Dim n = 2
        While File.Exists(candidate)
            candidate = Path.Combine(folder, $"{baseName} {n}{extension}")
            n += 1
        End While
        Return candidate
    End Function

    ''' <summary>Mischt verschachtelte Mehrkanal-Samples zu Mono (Mittelwert).</summary>
    Public Shared Function MixToMono(interleaved As IList(Of Single), channels As Integer) As Single()
        If channels <= 1 Then Return interleaved.ToArray()
        Dim frames = interleaved.Count \ channels
        Dim result(frames - 1) As Single
        For f = 0 To frames - 1
            Dim sum = 0.0F
            For c = 0 To channels - 1
                sum += interleaved(f * channels + c)
            Next
            result(f) = sum / channels
        Next
        Return result
    End Function

    ''' <summary>
    ''' Bereich mit Ton: schneidet Stille am Anfang und Ende ab (Pegel unter <paramref name="thresholdDb"/>),
    ''' lässt <paramref name="padMs"/> Rand stehen. Ganz stille Aufnahme → Länge 0.
    ''' </summary>
    Public Shared Function FindSoundRange(samples As Single(), sampleRate As Integer,
                                   Optional thresholdDb As Double = -45, Optional padMs As Double = 80) As (Start As Integer, Length As Integer)
        Dim threshold = CSng(NoiseGate.DbToLinear(thresholdDb))
        Dim first = -1, last = -1
        For i = 0 To samples.Length - 1
            If Math.Abs(samples(i)) >= threshold Then
                If first < 0 Then first = i
                last = i
            End If
        Next
        If first < 0 Then Return (0, 0)
        Dim pad = CInt(padMs * sampleRate / 1000)
        Dim s = Math.Max(0, first - pad)
        Dim e = Math.Min(samples.Length - 1, last + pad)
        Return (s, e - s + 1)
    End Function

    ''' <summary>
    ''' Hebt die Lautstärke an, bis die lauteste Stelle bei <paramref name="targetDb"/> liegt (höchstens um
    ''' <paramref name="maxGainDb"/>, damit Rauschen nicht hochgezogen wird). Liefert die angewandte Verstärkung.
    ''' </summary>
    Public Shared Function Normalize(samples As Single(), Optional targetDb As Double = -1, Optional maxGainDb As Double = 20) As Single
        Dim peak = 0.0F
        For Each v In samples
            Dim a = Math.Abs(v)
            If a > peak Then peak = a
        Next
        If peak <= 0 Then Return 1.0F
        Dim gain = CSng(Math.Min(NoiseGate.DbToLinear(targetDb) / peak, NoiseGate.DbToLinear(maxGainDb)))
        For i = 0 To samples.Length - 1
            samples(i) *= gain
        Next
        Return gain
    End Function

    ''' <summary>Kurzes Ein-/Ausblenden gegen Knackser an den Schnittkanten.</summary>
    Public Shared Sub FadeEdges(samples As Single(), sampleRate As Integer, Optional ms As Double = 10)
        Dim n = Math.Min(samples.Length \ 2, CInt(ms * sampleRate / 1000))
        For i = 0 To n - 1
            Dim g = CSng(i / n)
            samples(i) *= g
            samples(samples.Length - 1 - i) *= g
        Next
    End Sub
End Class
