' Laden, Speichern, Import/Export der Konfiguration als JSON.
Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

Public Module ConfigStore
    Public Const KeyCount As Integer = 15

    Public ReadOnly JsonOptions As New JsonSerializerOptions With {
        .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        .PropertyNameCaseInsensitive = True,
        .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        .WriteIndented = True,
        .ReadCommentHandling = JsonCommentHandling.Skip,
        .AllowTrailingCommas = True,
        .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    }

    Public ReadOnly Property DefaultPath As String
        Get
            Return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamDeck", "config.json")
        End Get
    End Property

    Public Function Serialize(config As DeckConfig) As String
        Return JsonSerializer.Serialize(config, JsonOptions)
    End Function

    ''' <summary>Liest JSON und repariert fehlende Teile. Wirft JsonException bei kaputtem JSON.</summary>
    Public Function Deserialize(json As String) As DeckConfig
        Dim cfg = JsonSerializer.Deserialize(Of DeckConfig)(json, JsonOptions)
        If cfg Is Nothing Then Throw New JsonException("Leere Konfiguration")
        Normalize(cfg)
        Return cfg
    End Function

    Public Function CloneConfig(config As DeckConfig) As DeckConfig
        Return Deserialize(Serialize(config))
    End Function

    Public Function CloneAction(a As ActionConfig) As ActionConfig
        If a Is Nothing Then Return Nothing
        Return JsonSerializer.Deserialize(Of ActionConfig)(JsonSerializer.Serialize(a, JsonOptions), JsonOptions)
    End Function

    ''' <summary>Sorgt für mindestens ein Profil mit mindestens einer Seite und gültige Werte.</summary>
    Public Sub Normalize(cfg As DeckConfig)
        If cfg.Profiles Is Nothing Then cfg.Profiles = New List(Of ProfileConfig)
        cfg.Profiles.RemoveAll(Function(p) p Is Nothing)
        If cfg.Profiles.Count = 0 Then cfg.Profiles.Add(New ProfileConfig With {.Name = "Standard"})
        If cfg.Obs Is Nothing Then cfg.Obs = New ObsSettings
        If cfg.VoiceMix Is Nothing Then cfg.VoiceMix = New VoiceMixSettings
        cfg.VoiceMix.MicVolume = Math.Max(0, Math.Min(100, cfg.VoiceMix.MicVolume))
        If cfg.LongPressMs < 100 OrElse cfg.LongPressMs > 10000 Then cfg.LongPressMs = 500
        If cfg.DoublePressMs < 150 OrElse cfg.DoublePressMs > 1000 Then cfg.DoublePressMs = 300
        If cfg.FolderBackKey < 0 OrElse cfg.FolderBackKey >= KeyCount Then cfg.FolderBackKey = 0

        For Each p In cfg.Profiles
            If String.IsNullOrWhiteSpace(p.Name) Then p.Name = "Profil"
            If String.IsNullOrWhiteSpace(p.AutoSwitchProcess) Then p.AutoSwitchProcess = Nothing
            If p.Pages Is Nothing Then p.Pages = New List(Of PageConfig)
            p.Pages.RemoveAll(Function(pg) pg Is Nothing)
            If p.Pages.Count = 0 Then p.Pages.Add(New PageConfig With {.Name = "Start"})
            For Each pg In p.Pages
                If String.IsNullOrWhiteSpace(pg.Name) Then pg.Name = "Seite"
                If pg.Keys Is Nothing Then pg.Keys = New Dictionary(Of String, KeyConfig)
                ' ungültige Tastennummern und leere Einträge entfernen
                For Each id In pg.Keys.Keys.ToList()
                    Dim n As Integer
                    If pg.Keys(id) Is Nothing OrElse Not Integer.TryParse(id, n) OrElse n < 0 OrElse n >= KeyCount Then pg.Keys.Remove(id)
                Next
                If Not pg.IsFolder Then pg.Folder = Nothing
            Next
            ' Die Startseite kann kein Ordner sein (sonst gäbe es kein "Zurück").
            p.Pages(0).Folder = Nothing
            ' Ordner haben immer die feste Zurück-Taste.
            For Each pg In p.Pages.Where(Function(x) x.IsFolder)
                pg.SetKey(cfg.FolderBackKey, FolderBackKeyConfig())
            Next
        Next

        If cfg.FindProfile(cfg.ActiveProfile) Is Nothing Then cfg.ActiveProfile = cfg.Profiles(0).Name
    End Sub

    ''' <summary>Die feste Zurück-Taste in Ordnern.</summary>
    Public Function FolderBackKeyConfig() As KeyConfig
        Return New KeyConfig With {.Label = "Zurück", .Press = New ActionConfig With {.ActionType = ActionTypes.Page, .Target = ActionTypes.PageBack}}
    End Function

    ''' <summary>Ist das die feste Zurück-Taste eines Ordners (nicht bearbeitbar)?</summary>
    Public Function IsFixedBackKey(cfg As DeckConfig, page As PageConfig, index As Integer) As Boolean
        Return page IsNot Nothing AndAlso page.IsFolder AndAlso index = cfg.FolderBackKey
    End Function

    ''' <summary>Lädt die Datei. Fehlt sie, wird die Standardkonfiguration angelegt und gespeichert.</summary>
    Public Function Load(path As String) As DeckConfig
        If Not File.Exists(path) Then
            Dim cfg = CreateDefault()
            Save(cfg, path)
            Return cfg
        End If
        Return Deserialize(File.ReadAllText(path))
    End Function

    ''' <summary>Speichert atomar (erst temporäre Datei, dann ersetzen), damit nie eine halbe Datei entsteht.</summary>
    Public Sub Save(cfg As DeckConfig, path As String)
        Dim dir = IO.Path.GetDirectoryName(path)
        If Not String.IsNullOrEmpty(dir) Then Directory.CreateDirectory(dir)
        Dim tmp = path & ".tmp"
        File.WriteAllText(tmp, Serialize(cfg), New System.Text.UTF8Encoding(False))
        If File.Exists(path) Then
            File.Replace(tmp, path, path & ".bak", True)
        Else
            File.Move(tmp, path)
        End If
    End Sub

    Public Function CreateDefault() As DeckConfig
        Dim start As New PageConfig With {.Name = "Start"}
        start.SetKey(0, New KeyConfig With {.Label = "Mute", .Press = New ActionConfig With {.ActionType = "media", .MediaKey = "mute"}})
        start.SetKey(1, New KeyConfig With {.Label = "Play/Pause", .Press = New ActionConfig With {.ActionType = "media", .MediaKey = "playpause"}})
        start.SetKey(2, New KeyConfig With {.Label = "Rechner", .Press = New ActionConfig With {.ActionType = "launch", .Path = "calc.exe"}})
        start.SetKey(3, New KeyConfig With {.Label = "Leiser", .Press = New ActionConfig With {.ActionType = "media", .MediaKey = "voldown"}})
        start.SetKey(4, New KeyConfig With {.Label = "Lauter", .Press = New ActionConfig With {.ActionType = "media", .MediaKey = "volup"}})
        start.SetKey(14, New KeyConfig With {.Label = "Mehr", .Press = New ActionConfig With {.ActionType = "page", .Target = "Seite 2"}})

        Dim page2 As New PageConfig With {.Name = "Seite 2"}
        page2.SetKey(0, New KeyConfig With {.Label = "Explorer", .Press = New ActionConfig With {.ActionType = "launch", .Path = "explorer.exe"}})
        page2.SetKey(14, New KeyConfig With {.Label = "Zurück", .Press = New ActionConfig With {.ActionType = "page", .Target = ActionTypes.PageBack}})

        Dim cfg As New DeckConfig With {.ActiveProfile = "Standard"}
        cfg.Profiles.Add(New ProfileConfig With {.Name = "Standard", .Pages = New List(Of PageConfig) From {start, page2}})
        Return cfg
    End Function
End Module
