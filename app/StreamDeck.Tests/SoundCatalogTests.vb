' Tests für den Sound-Katalog (Sounds\katalog.json) der Sound-Galerie.
Imports System.IO
Imports StreamDeck.Core
Imports Xunit

Public Class SoundCatalogTests
    Implements IDisposable

    Private ReadOnly _dir As String = Path.Combine(Path.GetTempPath(), "sdcat-" & Guid.NewGuid().ToString("N"))

    Public Sub New()
        Directory.CreateDirectory(_dir)
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        Try
            Directory.Delete(_dir, True)
        Catch
        End Try
    End Sub

    Private Sub Touch(ParamArray names As String())
        For Each n In names
            File.WriteAllBytes(Path.Combine(_dir, n), {0})
        Next
    End Sub

    Private Sub WriteCatalog(json As String)
        File.WriteAllText(Path.Combine(_dir, SoundCatalog.FileName), json)
    End Sub

    <Fact>
    Public Sub UsesCatalogOrderNamesAndCategories()
        Touch("b.mp3", "a.mp3")
        WriteCatalog("{""categories"":[""Fail"",""Hype""],""sounds"":[" &
                     "{""file"":""b.mp3"",""name"":""Bass-Boom"",""category"":""Fail"",""icon"":""💣""}," &
                     "{""file"":""a.mp3"",""name"":""Airhorn"",""category"":""Hype""}]}")
        Dim c = SoundCatalog.Load(_dir, "Sounds")
        Assert.Equal({"Fail", "Hype"}, c.Categories)
        Assert.Equal({"Bass-Boom", "Airhorn"}, c.Sounds.Select(Function(s) s.Name))
        Assert.Equal(Path.Combine("Sounds", "b.mp3"), c.Sounds(0).File)
        Assert.Equal("💣", c.Sounds(0).Icon)
    End Sub

    <Fact>
    Public Sub FilesWithoutEntryGoToOtherAndMissingFilesAreSkipped()
        Touch("eigener-sound.wav", "a.mp3", "notiz.txt")
        WriteCatalog("{""categories"":[""Hype"",""Leer""],""sounds"":[{""file"":""a.mp3"",""name"":""A"",""category"":""Hype""},{""file"":""weg.mp3"",""name"":""Weg"",""category"":""Hype""}]}")
        Dim c = SoundCatalog.Load(_dir, "Sounds")
        Assert.Equal({"Hype", SoundCatalog.OtherCategory}, c.Categories)    ' leere Kategorie ausgeblendet
        Assert.Equal(2, c.Sounds.Count)
        Dim own = c.Sounds(1)
        Assert.Equal("Eigener sound", own.Name)
        Assert.Equal(SoundCatalog.OtherCategory, own.Category)
    End Sub

    <Fact>
    Public Sub WorksWithoutOrWithBrokenCatalog()
        Touch("z.mp3", "a.mp3")
        Assert.Equal({"A", "Z"}, SoundCatalog.Load(_dir, "Sounds").Sounds.Select(Function(s) s.Name))
        WriteCatalog("{ kaputt")
        Assert.Equal(2, SoundCatalog.Load(_dir, "Sounds").Sounds.Count)
        Assert.Empty(SoundCatalog.Load(Path.Combine(_dir, "gibtsnicht"), "Sounds").Sounds)
    End Sub

    <Fact>
    Public Sub FilterByCategoryAndUmlautInsensitiveSearch()
        Touch("a.mp3", "b.mp3", "c.mp3")
        WriteCatalog("{""sounds"":[" &
                     "{""file"":""a.mp3"",""name"":""Tür knarrt"",""category"":""Spannung""}," &
                     "{""file"":""b.mp3"",""name"":""Tür knallt"",""category"":""Alltag""}," &
                     "{""file"":""c.mp3"",""name"":""Airhorn"",""category"":""Hype""}]}")
        Dim c = SoundCatalog.Load(_dir, "Sounds")
        Assert.Equal(2, c.Filter(Nothing, "tuer").Count)
        Assert.Equal(2, c.Filter(Nothing, "TÜR").Count)
        Assert.Single(c.Filter("Alltag", "tür"))
        Assert.Single(c.Filter(Nothing, "tür spannung"))
        Assert.Equal(3, c.Filter(Nothing, "  ").Count)
        Assert.Empty(c.Filter("Hype", "tür"))
    End Sub

    <Fact>
    Public Sub ShippedCatalogMatchesShippedFiles()
        ' Der echte Katalog im App-Projekt: jede Datei hat einen Eintrag mit Symbol, keine landet unter „Sonstige“.
        Dim dir = AppContext.BaseDirectory
        Dim soundsDir As String = Nothing
        While dir IsNot Nothing
            Dim candidate = Path.Combine(dir, "StreamDeck.App", "Sounds")
            If Directory.Exists(candidate) Then soundsDir = candidate : Exit While
            dir = Path.GetDirectoryName(dir)
        End While
        Assert.NotNull(soundsDir)
        Dim c = SoundCatalog.Load(soundsDir, "Sounds")
        Assert.DoesNotContain(SoundCatalog.OtherCategory, c.Categories)
        Assert.True(c.Sounds.Count >= 100, $"nur {c.Sounds.Count} Sounds")
        Assert.All(c.Sounds, Sub(s) Assert.False(String.IsNullOrWhiteSpace(s.Icon), s.File))
    End Sub
End Class
