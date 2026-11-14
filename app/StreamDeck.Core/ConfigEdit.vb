' Bearbeitungsfunktionen für Profile und Seiten, die Verweise (page-/profile-Aktionen) mitziehen.

Public Module ConfigEdit
    ''' <summary>Alle Aktionen einer Taste einschließlich der Schritte in Mehrfach-Aktionen.</summary>
    Public Iterator Function AllActions(key As KeyConfig) As IEnumerable(Of ActionConfig)
        If key Is Nothing Then Return
        For Each a In Flatten(key.Press)
            Yield a
        Next
        For Each a In Flatten(key.LongPress)
            Yield a
        Next
        For Each a In Flatten(key.DoublePress)
            Yield a
        Next
        For Each a In Flatten(key.Release)
            Yield a
        Next
    End Function

    Public Iterator Function Flatten(action As ActionConfig, Optional depth As Integer = 0) As IEnumerable(Of ActionConfig)
        If action Is Nothing OrElse depth > ActionRunner.MaxNesting Then Return
        Yield action
        If action.Actions Is Nothing Then Return
        For Each child In action.Actions
            For Each a In Flatten(child, depth + 1)
                Yield a
            Next
        Next
    End Function

    Private Iterator Function AllActions(profile As ProfileConfig) As IEnumerable(Of ActionConfig)
        For Each pg In profile.Pages
            For Each k In pg.Keys.Values
                For Each a In AllActions(k)
                    Yield a
                Next
            Next
        Next
    End Function

    ''' <summary>"Seite 2", "Seite 3" … – erster freier Name.</summary>
    Public Function UniqueName(existing As IEnumerable(Of String), baseName As String) As String
        Dim taken As New HashSet(Of String)(existing, StringComparer.OrdinalIgnoreCase)
        If Not taken.Contains(baseName) Then Return baseName
        Dim i = 2
        While taken.Contains($"{baseName} {i}")
            i += 1
        End While
        Return $"{baseName} {i}"
    End Function

    ''' <summary>Prüft einen neuen Namen. Liefert Nothing, wenn er gültig ist, sonst eine Fehlermeldung.</summary>
    Public Function ValidateName(newName As String, existing As IEnumerable(Of String), Optional currentName As String = Nothing) As String
        If String.IsNullOrWhiteSpace(newName) Then Return "Der Name darf nicht leer sein."
        If newName.Trim().StartsWith("@"c) Then Return "Namen mit @ am Anfang sind reserviert."
        For Each n In existing
            If String.Equals(n, newName.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
               Not String.Equals(n, currentName, StringComparison.OrdinalIgnoreCase) Then Return $"'{newName.Trim()}' gibt es schon."
        Next
        Return Nothing
    End Function

    Public Function AddPage(profile As ProfileConfig, Optional baseName As String = "Seite") As PageConfig
        Dim page As New PageConfig With {.Name = UniqueName(profile.Pages.Select(Function(p) p.Name), baseName)}
        profile.Pages.Add(page)
        Return page
    End Function

    ''' <summary>
    ''' Seite zum Ordner machen (oder zurück zur normalen Seite). Liegt auf der festen Zurück-Taste schon
    ''' eine Belegung, wird sie auf die erste freie Taste verschoben. Liefert False, wenn dafür kein Platz war
    ''' (die Belegung geht dann verloren) oder die Seite die Startseite ist.
    ''' </summary>
    Public Function SetFolder(cfg As DeckConfig, profile As ProfileConfig, pageName As String, folder As Boolean) As Boolean
        Dim page = profile.FindPage(pageName)
        If page Is Nothing Then Return False
        If Not folder Then
            page.Folder = Nothing
            ' die Zurück-Taste bleibt als normale Taste stehen
            Return True
        End If
        If page Is profile.Pages(0) Then Return False
        page.Folder = True
        Dim occupying = page.GetKey(cfg.FolderBackKey)
        If occupying Is Nothing OrElse IsBackKey(occupying) Then
            page.SetKey(cfg.FolderBackKey, ConfigStore.FolderBackKeyConfig())
            Return True
        End If
        Dim free = Enumerable.Range(0, ConfigStore.KeyCount).Where(Function(i) i <> cfg.FolderBackKey AndAlso page.GetKey(i) Is Nothing).DefaultIfEmpty(-1).First()
        If free >= 0 Then page.SetKey(free, occupying)
        page.SetKey(cfg.FolderBackKey, ConfigStore.FolderBackKeyConfig())
        Return free >= 0
    End Function

    ''' <summary>Feste Zurück-Taste der Ordner verlegen: alte Zurück-Taste entfernen, neue setzen (Belegungen werden verschoben).</summary>
    Public Sub SetFolderBackKey(cfg As DeckConfig, newKey As Integer)
        If newKey < 0 OrElse newKey >= ConfigStore.KeyCount OrElse newKey = cfg.FolderBackKey Then Return
        Dim old = cfg.FolderBackKey
        cfg.FolderBackKey = newKey
        For Each p In cfg.Profiles
            For Each pg In p.Pages.Where(Function(x) x.IsFolder).ToList()
                If IsBackKey(pg.GetKey(old)) Then pg.SetKey(old, Nothing)
                SetFolder(cfg, p, pg.Name, True)
            Next
        Next
    End Sub

    ''' <summary>Hülle für eine Profil-Datei (*.sdprofile.json), damit sie sich von einer ganzen Konfiguration unterscheiden lässt.</summary>
    Public Class ProfileFile
        Public Property StreamDeckProfile As Integer = 1
        Public Property Profile As ProfileConfig
    End Class

    ''' <summary>Ein Profil als JSON zum Weitergeben an einen anderen Rechner.</summary>
    Public Function ExportProfile(profile As ProfileConfig) As String
        Return Text.Json.JsonSerializer.Serialize(New ProfileFile With {.Profile = profile}, ConfigStore.JsonOptions)
    End Function

    ''' <summary>
    ''' Profil aus JSON übernehmen (Profil-Datei oder nacktes Profil). Gibt es den Namen schon, wird er
    ''' eindeutig gemacht ("Name 2"). Liefert das hinzugefügte Profil. Wirft JsonException/FormatException.
    ''' </summary>
    Public Function ImportProfile(cfg As DeckConfig, json As String) As ProfileConfig
        Dim file = Text.Json.JsonSerializer.Deserialize(Of ProfileFile)(json, ConfigStore.JsonOptions)
        Dim p = file?.Profile
        If p Is Nothing Then
            p = Text.Json.JsonSerializer.Deserialize(Of ProfileConfig)(json, ConfigStore.JsonOptions)
            If p Is Nothing OrElse p.Pages Is Nothing OrElse p.Pages.Count = 0 Then Throw New FormatException("Die Datei enthält kein Profil.")
        End If
        Dim baseName = If(String.IsNullOrWhiteSpace(p.Name), "Profil", p.Name.Trim())
        Dim existing = cfg.Profiles.Select(Function(x) x.Name).ToList()
        p.Name = If(existing.Contains(baseName, StringComparer.OrdinalIgnoreCase), UniqueName(existing, baseName), baseName)
        cfg.Profiles.Add(p)
        ConfigStore.Normalize(cfg)
        Return p
    End Function

    Private Function IsBackKey(k As KeyConfig) As Boolean
        Return k IsNot Nothing AndAlso k.Press IsNot Nothing AndAlso IsType(k.Press, ActionTypes.Page) AndAlso String.Equals(k.Press.Target, ActionTypes.PageBack, StringComparison.OrdinalIgnoreCase) AndAlso k.LongPress Is Nothing
    End Function

    ''' <summary>Benennt eine Seite um und passt alle Seitenwechsel im Profil an.</summary>
    Public Sub RenamePage(profile As ProfileConfig, oldName As String, newName As String)
        Dim page = profile.FindPage(oldName)
        If page Is Nothing Then Throw New ArgumentException($"Seite '{oldName}' gibt es nicht.")
        Dim err = ValidateName(newName, profile.Pages.Select(Function(p) p.Name), page.Name)
        If err IsNot Nothing Then Throw New ArgumentException(err)
        newName = newName.Trim()
        For Each a In AllActions(profile)
            If IsType(a, ActionTypes.Page) AndAlso String.Equals(a.Target, page.Name, StringComparison.OrdinalIgnoreCase) Then a.Target = newName
        Next
        page.Name = newName
    End Sub

    ''' <summary>Löscht eine Seite (nicht die letzte). Liefert False, wenn das nicht geht.</summary>
    Public Function DeletePage(profile As ProfileConfig, name As String) As Boolean
        If profile.Pages.Count <= 1 Then Return False
        Dim page = profile.FindPage(name)
        If page Is Nothing Then Return False
        profile.Pages.Remove(page)
        Return True
    End Function

    Public Function AddProfile(cfg As DeckConfig, Optional baseName As String = "Profil") As ProfileConfig
        Dim p As New ProfileConfig With {.Name = UniqueName(cfg.Profiles.Select(Function(x) x.Name), baseName)}
        p.Pages.Add(New PageConfig With {.Name = "Start"})
        cfg.Profiles.Add(p)
        Return p
    End Function

    ''' <summary>Benennt ein Profil um und passt aktives Profil und Profilwechsel-Aktionen an.</summary>
    Public Sub RenameProfile(cfg As DeckConfig, oldName As String, newName As String)
        Dim profile = cfg.FindProfile(oldName)
        If profile Is Nothing Then Throw New ArgumentException($"Profil '{oldName}' gibt es nicht.")
        Dim err = ValidateName(newName, cfg.Profiles.Select(Function(p) p.Name), profile.Name)
        If err IsNot Nothing Then Throw New ArgumentException(err)
        newName = newName.Trim()
        For Each p In cfg.Profiles
            For Each a In AllActions(p)
                If IsType(a, ActionTypes.Profile) AndAlso String.Equals(a.Target, profile.Name, StringComparison.OrdinalIgnoreCase) Then a.Target = newName
            Next
        Next
        If String.Equals(cfg.ActiveProfile, profile.Name, StringComparison.OrdinalIgnoreCase) Then cfg.ActiveProfile = newName
        profile.Name = newName
    End Sub

    Public Function DeleteProfile(cfg As DeckConfig, name As String) As Boolean
        If cfg.Profiles.Count <= 1 Then Return False
        Dim p = cfg.FindProfile(name)
        If p Is Nothing Then Return False
        cfg.Profiles.Remove(p)
        If String.Equals(cfg.ActiveProfile, p.Name, StringComparison.OrdinalIgnoreCase) Then cfg.ActiveProfile = cfg.Profiles(0).Name
        Return True
    End Function

    Private Function IsType(a As ActionConfig, t As String) As Boolean
        Return String.Equals(a.ActionType, t, StringComparison.OrdinalIgnoreCase)
    End Function
End Module
