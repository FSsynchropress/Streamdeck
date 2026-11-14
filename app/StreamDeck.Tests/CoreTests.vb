Imports System.IO
Imports StreamDeck.Core
Imports Xunit

Public Class ProtocolTests
    <Fact>
    Public Sub ParsesHello()
        Dim m = DeviceProtocol.Parse("HELLO STREAMDECK 1 15")
        Assert.Equal(MessageKind.Hello, m.Kind)
        Assert.Equal(1, m.ProtocolVersion)
        Assert.Equal(15, m.KeyCount)
    End Sub

    <Theory>
    <InlineData("D 0", MessageKind.KeyDown, 0)>
    <InlineData("U 14", MessageKind.KeyUp, 14)>
    <InlineData("D 3" & vbCr, MessageKind.KeyDown, 3)>
    <InlineData("  U 7  ", MessageKind.KeyUp, 7)>
    Public Sub ParsesKeyEvents(line As String, kind As MessageKind, key As Integer)
        Dim m = DeviceProtocol.Parse(line)
        Assert.Equal(kind, m.Kind)
        Assert.Equal(key, m.KeyIndex)
    End Sub

    <Fact>
    Public Sub ParsesPong()
        Assert.Equal(MessageKind.Pong, DeviceProtocol.Parse("PONG").Kind)
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData(Nothing)>
    <InlineData("D")>
    <InlineData("D x")>
    <InlineData("D -1")>
    <InlineData("D 999")>
    <InlineData("STATE 000000000000000")>
    <InlineData("KEYTEST KEY 3 OK GPIO 5 EDGES 1 (4/15)")>
    <InlineData("INFO FW 1.0.0 PROTO 1 KEYS 15")>
    <InlineData("# Hilfe")>
    <InlineData("HELLO STREAMDECK")>
    <InlineData("HELLO OTHERDEVICE 1 15")>
    <InlineData("TEST OK 15 KEYS")>
    Public Sub IgnoresEverythingElse(line As String)
        Assert.Equal(MessageKind.Unknown, DeviceProtocol.Parse(line).Kind)
    End Sub

    <Fact>
    Public Sub LedCommandIsClamped()
        Assert.Equal("LED 255 0 12", DeviceProtocol.LedCommand(300, -5, 12))
    End Sub
End Class

Public Class KeyPressTrackerTests
    Private Shared Function Tracker(ParamArray keysWithLong As Integer()) As KeyPressTracker
        Return New KeyPressTracker(Function(k) keysWithLong.Contains(k)) With {.LongPressMs = 500}
    End Function

    <Fact>
    Public Sub KeyWithoutLongActionFiresImmediatelyOnDown()
        Dim t = Tracker()
        Assert.Equal(PressKind.ShortPress, t.OnDown(2, 1000))
        Assert.Null(t.OnUp(2, 1050))
        Assert.Empty(t.Tick(5000))
    End Sub

    <Fact>
    Public Sub KeyWithLongActionShortWhenReleasedEarly()
        Dim t = Tracker(2)
        Assert.Null(t.OnDown(2, 1000))
        Assert.Empty(t.Tick(1499))
        Assert.Equal(PressKind.ShortPress, t.OnUp(2, 1499))
        Assert.Empty(t.Tick(3000))
    End Sub

    <Fact>
    Public Sub KeyWithLongActionLongWhenHeld()
        Dim t = Tracker(2)
        t.OnDown(2, 1000)
        Assert.True(t.Tick(1500).SequenceEqual({2}))
        Assert.Null(t.OnUp(2, 2000))  ' danach kein kurzer Druck mehr
        Assert.Empty(t.Tick(3000))
    End Sub

    <Fact>
    Public Sub LateReleaseWithoutTickCountsAsLong()
        Dim t = Tracker(1)
        t.OnDown(1, 0)
        Assert.Equal(PressKind.LongPress, t.OnUp(1, 800))
    End Sub

    <Fact>
    Public Sub IndependentKeys()
        Dim t = Tracker(1, 2)
        t.OnDown(1, 0)
        t.OnDown(2, 300)
        Assert.True(t.Tick(500).SequenceEqual({1}))
        Assert.Equal(PressKind.ShortPress, t.OnUp(2, 600))
        Assert.Equal(0, t.PendingCount)
    End Sub

    <Fact>
    Public Sub UpWithoutDownIsIgnored()
        Assert.Null(Tracker(1).OnUp(1, 100))
    End Sub

    <Fact>
    Public Sub ResetForgetsPendingKeys()
        Dim t = Tracker(1)
        t.OnDown(1, 0)
        t.Reset()
        Assert.Empty(t.Tick(1000))
        Assert.Null(t.OnUp(1, 1000))
    End Sub
End Class

Public Class ConfigTests
    ' Beispiel aus CLAUDE.md
    Private Const ClaudeMdExample As String = "{
      ""activeProfile"": ""Standard"",
      ""profiles"": [
        {
          ""name"": ""Standard"",
          ""autoSwitchProcess"": null,
          ""pages"": [
            {
              ""name"": ""Start"",
              ""keys"": {
                ""0"": { ""label"": ""Mute"",   ""press"": { ""type"": ""media"", ""key"": ""mute"" } },
                ""1"": { ""label"": ""Tusch"",  ""press"": { ""type"": ""sound"", ""file"": ""C:\\Sounds\\tusch.mp3"", ""volume"": 80 } },
                ""2"": { ""label"": ""Rechner"",""press"": { ""type"": ""launch"", ""path"": ""calc.exe"" } },
                ""14"": { ""label"": ""Mehr"",  ""press"": { ""type"": ""page"", ""target"": ""Seite 2"" } }
              }
            }
          ]
        }
      ]
    }"

    <Fact>
    Public Sub ParsesClaudeMdExample()
        Dim cfg = ConfigStore.Deserialize(ClaudeMdExample)
        Assert.Equal("Standard", cfg.ActiveProfile)
        Dim page = cfg.Profiles(0).Pages(0)
        Assert.Equal("media", page.GetKey(0).Press.ActionType)
        Assert.Equal("mute", page.GetKey(0).Press.MediaKey)
        Assert.Equal("C:\Sounds\tusch.mp3", page.GetKey(1).Press.File)
        Assert.Equal(80, page.GetKey(1).Press.Volume)
        Assert.Equal("calc.exe", page.GetKey(2).Press.Path)
        Assert.Equal("Seite 2", page.GetKey(14).Press.Target)
        Assert.Null(page.GetKey(5))
        Assert.Equal(500, cfg.LongPressMs)
        Assert.NotNull(cfg.Obs)
    End Sub

    <Fact>
    Public Sub RoundTripKeepsEverything()
        Dim cfg = ConfigStore.CreateDefault()
        cfg.Profiles(0).Pages(0).SetKey(5, New KeyConfig With {
            .Label = "Multi", .Icon = "C:\icon.png",
            .Press = New ActionConfig With {.ActionType = "multi", .Actions = New List(Of ActionConfig) From {
                New ActionConfig With {.ActionType = "hotkey", .Keys = "Ctrl+C"},
                New ActionConfig With {.ActionType = "wait", .Ms = 200},
                New ActionConfig With {.ActionType = "text", .Text = "Grüße ""äöü"" <>&"}}},
            .LongPress = New ActionConfig With {.ActionType = "command", .Command = "echo hi", .Shell = "powershell", .Hidden = True}})
        Dim json = ConfigStore.Serialize(cfg)
        Dim back = ConfigStore.Deserialize(json)
        Assert.Equal(json, ConfigStore.Serialize(back))
        Dim k = back.Profiles(0).Pages(0).GetKey(5)
        Assert.Equal(3, k.Press.Actions.Count)
        Assert.Equal("Grüße ""äöü"" <>&", k.Press.Actions(2).Text)
        Assert.True(k.LongPress.Hidden)
        Assert.Contains("""type"": ""multi""", json)
        Assert.DoesNotContain("""volume""", json)   ' leere Felder werden nicht geschrieben
        Assert.Contains("Grüße", json)               ' Umlaute bleiben lesbar
    End Sub

    <Fact>
    Public Sub NormalizeRepairsBrokenConfig()
        Dim cfg = ConfigStore.Deserialize("{ ""activeProfile"": ""Gibtsnicht"", ""longPressMs"": 5, ""profiles"": [ { ""name"": """", ""pages"": [] } ] }")
        Assert.Equal("Profil", cfg.ActiveProfile)
        Assert.Single(cfg.Profiles(0).Pages)
        Assert.Equal(500, cfg.LongPressMs)
    End Sub

    <Fact>
    Public Sub NormalizeDropsInvalidKeyIds()
        Dim cfg = ConfigStore.Deserialize("{ ""profiles"": [ { ""name"": ""A"", ""pages"": [ { ""name"": ""P"", ""keys"": { ""15"": {}, ""x"": {}, ""-1"": {}, ""3"": { ""label"": ""ok"" } } } ] } ] }")
        Assert.True(cfg.Profiles(0).Pages(0).Keys.Keys.SequenceEqual({"3"}))
    End Sub

    <Fact>
    Public Sub EmptyJsonGivesUsableConfig()
        Dim cfg = ConfigStore.Deserialize("{}")
        Assert.Single(cfg.Profiles)
        Assert.Equal(cfg.Profiles(0).Name, cfg.ActiveProfile)
    End Sub

    <Fact>
    Public Sub CommentsAndTrailingCommasAreAccepted()
        Dim cfg = ConfigStore.Deserialize("{ // Kommentar" & vbLf & """activeProfile"": ""A"", ""profiles"": [ { ""name"": ""A"", }, ], }")
        Assert.Equal("A", cfg.ActiveProfile)
    End Sub

    <Fact>
    Public Sub BrokenJsonThrows()
        Assert.ThrowsAny(Of Text.Json.JsonException)(Function() ConfigStore.Deserialize("{ kaputt"))
    End Sub

    <Fact>
    Public Sub LoadCreatesDefaultAndSaveIsAtomic()
        Dim dir = Path.Combine(Path.GetTempPath(), "sdtest_" & Guid.NewGuid().ToString("N"))
        Dim file = Path.Combine(dir, "config.json")
        Try
            Dim cfg = ConfigStore.Load(file)
            Assert.True(IO.File.Exists(file))
            cfg.ActiveProfile = "Standard"
            cfg.Profiles(0).Pages(0).SetKey(9, New KeyConfig With {.Label = "Neu"})
            ConfigStore.Save(cfg, file)
            Assert.False(IO.File.Exists(file & ".tmp"))
            Assert.True(IO.File.Exists(file & ".bak"))
            Assert.Equal("Neu", ConfigStore.Load(file).Profiles(0).Pages(0).GetKey(9).Label)
        Finally
            If Directory.Exists(dir) Then Directory.Delete(dir, True)
        End Try
    End Sub

    <Fact>
    Public Sub DefaultConfigIsValid()
        Dim cfg = ConfigStore.CreateDefault()
        Dim st As New DeckState(cfg)
        Assert.True(st.GoToPage(cfg.Profiles(0).Pages(0).GetKey(14).Press.Target))
        Assert.Equal("Seite 2", st.CurrentPage.Name)
        Assert.True(st.GoToPage(st.GetKey(14).Press.Target))
        Assert.Equal("Start", st.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub DescribeCoversAllTypes()
        For Each t In ActionTypes.All
            Assert.False(String.IsNullOrWhiteSpace(ActionTypes.Describe(New ActionConfig With {.ActionType = t})))
        Next
        Assert.Equal("(keine Aktion)", ActionTypes.Describe(Nothing))
    End Sub
End Class

Public Class DeckStateTests
    Private Shared Function MakeConfig() As DeckConfig
        Dim cfg As New DeckConfig With {.ActiveProfile = "Standard"}
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Standard", .Pages = New List(Of PageConfig) From {
            New PageConfig With {.Name = "Start"}, New PageConfig With {.Name = "A"}, New PageConfig With {.Name = "B"}}})
        cfg.Profiles.Add(New ProfileConfig With {.Name = "OBS", .AutoSwitchProcess = "obs64.exe",
            .Pages = New List(Of PageConfig) From {New PageConfig With {.Name = "Szenen"}}})
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Browser", .AutoSwitchProcess = "chrome; msedge",
            .Pages = New List(Of PageConfig) From {New PageConfig With {.Name = "Web"}}})
        Return cfg
    End Function

    <Fact>
    Public Sub StartsOnActiveProfileFirstPage()
        Dim st As New DeckState(MakeConfig())
        Assert.Equal("Standard", st.CurrentProfile.Name)
        Assert.Equal("Start", st.CurrentPage.Name)
        Assert.False(st.IsAutoProfile)
    End Sub

    <Fact>
    Public Sub PageNavigationWithBackHistory()
        Dim st As New DeckState(MakeConfig())
        Dim changes = 0
        AddHandler st.Changed, Sub() changes += 1
        Assert.True(st.GoToPage("A"))
        Assert.True(st.GoToPage("b"))           ' Groß-/Kleinschreibung egal
        Assert.Equal("B", st.CurrentPage.Name)
        Assert.True(st.GoToPage("@back"))
        Assert.Equal("A", st.CurrentPage.Name)
        Assert.True(st.Back())
        Assert.Equal("Start", st.CurrentPage.Name)
        Assert.False(st.Back())                  ' schon auf der ersten Seite
        Assert.Equal(4, changes)
    End Sub

    <Fact>
    Public Sub BackWithoutHistoryGoesToFirstPage()
        Dim cfg = MakeConfig()
        cfg.ActiveProfile = "Standard"
        Dim st As New DeckState(cfg)
        st.GoToPage("A")
        st.GoToPage("@first")
        Assert.Equal("Start", st.CurrentPage.Name)
        Assert.False(st.GoToPage("@back"))
    End Sub

    <Fact>
    Public Sub UnknownOrSamePageIsIgnored()
        Dim st As New DeckState(MakeConfig())
        Assert.False(st.GoToPage("Gibtsnicht"))
        Assert.False(st.GoToPage("Start"))
        Assert.False(st.GoToPage(""))
        Assert.Equal("Start", st.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub AutoSwitchByForegroundProcess()
        Dim st As New DeckState(MakeConfig())
        st.GoToPage("A")
        Assert.True(st.ApplyForegroundProcess("OBS64"))
        Assert.Equal("OBS", st.CurrentProfile.Name)
        Assert.True(st.IsAutoProfile)
        Assert.False(st.ApplyForegroundProcess("obs64.exe"))   ' schon aktiv
        Assert.True(st.ApplyForegroundProcess("msedge"))
        Assert.Equal("Browser", st.CurrentProfile.Name)
        Assert.True(st.ApplyForegroundProcess("notepad"))      ' kein Treffer -> zurück zum manuellen Profil
        Assert.Equal("Standard", st.CurrentProfile.Name)
        Assert.Equal("Start", st.CurrentPage.Name)
        Assert.False(st.IsAutoProfile)
        Assert.Equal("Standard", st.Config.ActiveProfile)      ' Automatik ändert die Auswahl nicht
    End Sub

    <Fact>
    Public Sub ManualSelectChangesActiveProfile()
        Dim st As New DeckState(MakeConfig())
        Assert.True(st.SelectProfile("browser"))
        Assert.Equal("Browser", st.Config.ActiveProfile)
        Assert.False(st.SelectProfile("gibtsnicht"))
        Assert.False(st.ApplyForegroundProcess("notepad"))
        Assert.Equal("Browser", st.CurrentProfile.Name)
    End Sub

    <Fact>
    Public Sub ReplaceConfigKeepsPageIfStillPresent()
        Dim st As New DeckState(MakeConfig())
        st.GoToPage("B")
        Dim cfg = ConfigStore.CloneConfig(st.Config)
        st.ReplaceConfig(cfg)
        Assert.Equal("B", st.CurrentPage.Name)
        cfg = ConfigStore.CloneConfig(st.Config)
        cfg.Profiles(0).Pages.RemoveAt(2)
        st.ReplaceConfig(cfg)
        Assert.Equal("Start", st.CurrentPage.Name)
    End Sub

    <Fact>
    Public Sub NormalizeProcessName()
        Assert.Equal("obs64", DeckState.NormalizeProcessName(" OBS64.EXE "))
        Assert.Equal("", DeckState.NormalizeProcessName(Nothing))
    End Sub
End Class

Public Class HotkeyTests
    <Fact>
    Public Sub ParsesCombinationModifiersFirst()
        Dim keys = Hotkeys.ParseHotkey("M+Shift+Ctrl")
        Assert.True(keys.OrderBy(Function(x) x).SequenceEqual(New UShort() {&H10US, &H11US, &H4DUS}))
        Assert.Equal(&H4DUS, keys.Last())
    End Sub

    <Theory>
    <InlineData("F13", &H7C)>
    <InlineData("F24", &H87)>
    <InlineData("f1", &H70)>
    <InlineData("Strg", &H11)>
    <InlineData("Entf", &H2E)>
    <InlineData("Num5", &H65)>
    <InlineData("7", &H37)>
    <InlineData("Plus", &HBB)>
    Public Sub ParsesSingleKeys(name As String, vk As Integer)
        Assert.Equal(CUShort(vk), Hotkeys.ParseHotkey(name).Single())
    End Sub

    <Theory>
    <InlineData("")>
    <InlineData("Ctrl+")>
    <InlineData("Ctrl+Foo")>
    <InlineData("Ctrl+Ctrl")>
    <InlineData("F25")>
    Public Sub RejectsInvalid(text As String)
        Dim keys As List(Of UShort) = Nothing
        Dim err As String = Nothing
        Assert.False(Hotkeys.TryParseHotkey(text, keys, err))
        Assert.False(String.IsNullOrEmpty(err))
    End Sub

    <Fact>
    Public Sub MediaKeys()
        Assert.Equal(&HADUS, Hotkeys.MediaKeyCode("mute"))
        Assert.Equal(&HB3US, Hotkeys.MediaKeyCode("PlayPause"))
        For Each n In Hotkeys.MediaKeyNames
            Hotkeys.MediaKeyCode(n)
        Next
        Assert.Throws(Of FormatException)(Function() Hotkeys.MediaKeyCode("lauter"))
    End Sub

    <Fact>
    Public Sub ExtendedKeys()
        Assert.True(Hotkeys.IsExtendedKey(&H25US))   ' Pfeil links
        Assert.False(Hotkeys.IsExtendedKey(&H41US))  ' A
    End Sub
End Class
