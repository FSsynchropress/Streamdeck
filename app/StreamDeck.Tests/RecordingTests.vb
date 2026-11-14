Imports System.IO
Imports StreamDeck.Core
Imports Xunit

Public Class RecordingTests
    <Fact>
    Public Sub TrimsSilenceWithPadding()
        Dim s(9999) As Single           ' 10 000 Samples bei 1000 Hz = 10 s
        For i = 4000 To 4999
            s(i) = 0.5F
        Next
        Dim r = RecordingTools.FindSoundRange(s, 1000, padMs:=100)
        Assert.Equal(3900, r.Start)
        Assert.Equal(1200, r.Length)
    End Sub

    <Fact>
    Public Sub SilentRecordingHasNoRange()
        Dim s(999) As Single
        s(10) = 0.001F                  ' -60 dB, unter der Schwelle
        Assert.Equal(0, RecordingTools.FindSoundRange(s, 1000).Length)
    End Sub

    <Fact>
    Public Sub PaddingStopsAtEdges()
        Dim s(99) As Single
        s(0) = 1 : s(99) = -1
        Dim r = RecordingTools.FindSoundRange(s, 1000, padMs:=50)
        Assert.Equal(0, r.Start)
        Assert.Equal(100, r.Length)
    End Sub

    <Fact>
    Public Sub NormalizesPeakToTarget()
        Dim s = {0.1F, -0.25F, 0.05F}
        RecordingTools.Normalize(s, targetDb:=0)
        Assert.Equal(-1.0F, s(1), 3)
        Assert.Equal(0.4F, s(0), 3)
    End Sub

    <Fact>
    Public Sub NormalizeLimitsGain()
        Dim s = {0.001F}
        Dim gain = RecordingTools.Normalize(s, targetDb:=0, maxGainDb:=20)
        Assert.Equal(10.0F, gain, 3)
        Assert.Equal(0.01F, s(0), 4)
    End Sub

    <Fact>
    Public Sub NormalizeKeepsSilence()
        Dim s = {0.0F, 0.0F}
        Assert.Equal(1.0F, RecordingTools.Normalize(s))
    End Sub

    <Fact>
    Public Sub MixesStereoToMono()
        Dim m = RecordingTools.MixToMono({1.0F, 0.0F, 0.5F, 0.5F, -1.0F, 1.0F}, 2)
        Assert.Equal("0.5 0.5 0", String.Join(" ", m.Select(Function(x) x.ToString(Globalization.CultureInfo.InvariantCulture))))
    End Sub

    <Fact>
    Public Sub FadesEdges()
        Dim s = Enumerable.Repeat(1.0F, 100).ToArray()
        RecordingTools.FadeEdges(s, 1000, ms:=10)
        Assert.Equal(0.0F, s(0))
        Assert.Equal(0.0F, s(99))
        Assert.Equal(1.0F, s(50))
    End Sub

    <Theory>
    <InlineData("Hallo Welt", "Hallo Welt")>
    <InlineData("  a:b?c*  ", "a_b_c_")>
    <InlineData("   ", "Aufnahme")>
    <InlineData("Ende...", "Ende")>
    Public Sub MakesSafeFileNames(input As String, expected As String)
        Assert.Equal(expected, RecordingTools.SafeFileName(input))
    End Sub

    <Fact>
    Public Sub FindsUniquePath()
        Dim dir = Path.Combine(Path.GetTempPath(), "sd-rec-" & Guid.NewGuid().ToString("N"))
        Directory.CreateDirectory(dir)
        Try
            Assert.Equal(Path.Combine(dir, "Hallo.wav"), RecordingTools.UniquePath(dir, "Hallo", ".wav"))
            File.WriteAllText(Path.Combine(dir, "Hallo.wav"), "")
            File.WriteAllText(Path.Combine(dir, "Hallo 2.wav"), "")
            Assert.Equal(Path.Combine(dir, "Hallo 3.wav"), RecordingTools.UniquePath(dir, "Hallo", ".wav"))
        Finally
            Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub ConfigPathUsesAppDataVariable()
        Assert.Equal("%APPDATA%\StreamDeck\sounds\x.wav", RecordingTools.ConfigPath("C:\Users\u\AppData\Roaming\StreamDeck\sounds\x.wav", "C:\Users\u\AppData\Roaming"))
        Assert.Equal("D:\x.wav", RecordingTools.ConfigPath("D:\x.wav", "C:\Users\u\AppData\Roaming"))
    End Sub

    <Fact>
    Public Sub ConfigPathResolvesBack()
        Dim full = Path.Combine(RecordingTools.Folder, "x.wav")
        Assert.Equal(full, ProcessActions.ResolveAppRelativePath(RecordingTools.ConfigPath(full), "C:\irgendwo"))
    End Sub
End Class
