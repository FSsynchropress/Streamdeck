' Aktuelles Profil + aktuelle Seite, Seitenverlauf ("Zurück") und automatische Profilwahl.

Public Class DeckState
    Private _config As DeckConfig
    Private ReadOnly _history As New Stack(Of String)
    Private _profile As ProfileConfig
    Private _page As PageConfig

    ''' <summary>Wird ausgelöst, wenn sich Profil oder Seite ändert.</summary>
    Public Event Changed As EventHandler

    Public Sub New(config As DeckConfig)
        ReplaceConfig(config)
    End Sub

    Public ReadOnly Property Config As DeckConfig
        Get
            Return _config
        End Get
    End Property

    Public ReadOnly Property CurrentProfile As ProfileConfig
        Get
            Return _profile
        End Get
    End Property

    Public ReadOnly Property CurrentPage As PageConfig
        Get
            Return _page
        End Get
    End Property

    ''' <summary>True, solange ein Profil durch die Automatik (Vordergrundprogramm) aktiv ist.</summary>
    Public ReadOnly Property IsAutoProfile As Boolean
        Get
            Return Not String.Equals(_profile.Name, _config.ActiveProfile, StringComparison.OrdinalIgnoreCase)
        End Get
    End Property

    ''' <summary>
    ''' Neue Konfiguration übernehmen; Profil und Seite bleiben erhalten, wenn es sie noch gibt.
    ''' Mit profileName/pageName wird stattdessen gezielt diese Seite gewählt (z. B. nach dem Umbenennen).
    ''' </summary>
    Public Sub ReplaceConfig(config As DeckConfig, Optional profileName As String = Nothing, Optional pageName As String = Nothing)
        ConfigStore.Normalize(config)
        Dim oldProfile = If(profileName, _profile?.Name)
        Dim oldPage = If(pageName, _page?.Name)
        _config = config

        _profile = If(If(oldProfile IsNot Nothing, config.FindProfile(oldProfile), Nothing), config.FindProfile(config.ActiveProfile))
        Dim samePage = If(oldPage IsNot Nothing, _profile.FindPage(oldPage), Nothing)
        If samePage Is Nothing Then _history.Clear()
        _page = If(samePage, _profile.Pages(0))
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    Public Function GetKey(index As Integer) As KeyConfig
        Return _page.GetKey(index)
    End Function

    ''' <summary>Profil manuell wählen (wird als aktives Profil gespeichert).</summary>
    Public Function SelectProfile(name As String) As Boolean
        Dim p As ProfileConfig
        Dim delta = RelativeDelta(name)
        If delta <> 0 Then
            If _config.Profiles.Count < 2 Then Return False
            Dim i = _config.Profiles.IndexOf(_profile)
            p = _config.Profiles((i + delta + _config.Profiles.Count) Mod _config.Profiles.Count)
        Else
            p = _config.FindProfile(name)
        End If
        If p Is Nothing Then Return False
        _config.ActiveProfile = p.Name
        SetProfile(p)
        Return True
    End Function

    Private Sub SetProfile(p As ProfileConfig)
        If p Is _profile Then Return
        _profile = p
        _page = p.Pages(0)
        _history.Clear()
        RaiseEvent Changed(Me, EventArgs.Empty)
    End Sub

    ''' <summary>"@next" -> 1, "@prev" -> -1, sonst 0.</summary>
    Private Shared Function RelativeDelta(target As String) As Integer
        If String.Equals(target, ActionTypes.TargetNext, StringComparison.OrdinalIgnoreCase) Then Return 1
        If String.Equals(target, ActionTypes.TargetPrev, StringComparison.OrdinalIgnoreCase) Then Return -1
        Return 0
    End Function

    ''' <summary>Seite wechseln. Versteht "@back", "@first", "@next" und "@prev" (Blättern überspringt Ordner, ringsum).</summary>
    Public Function GoToPage(target As String) As Boolean
        If String.IsNullOrWhiteSpace(target) Then Return False
        Dim delta = RelativeDelta(target)
        If delta <> 0 Then
            Dim pages = _profile.Pages.Where(Function(x) Not x.IsFolder).ToList()
            Dim i = pages.IndexOf(_page)
            ' Aus einem Ordner heraus: von der Seite aus blättern, aus der der Ordner geöffnet wurde
            If i < 0 Then i = pages.IndexOf(If(_history.Select(Function(n) _profile.FindPage(n)).FirstOrDefault(Function(x) x IsNot Nothing AndAlso Not x.IsFolder), pages(0)))
            Dim nextPage = pages((i + delta + pages.Count) Mod pages.Count)
            If nextPage Is _page Then Return False
            _history.Clear()
            _page = nextPage
            RaiseEvent Changed(Me, EventArgs.Empty)
            Return True
        End If
        If String.Equals(target, ActionTypes.PageBack, StringComparison.OrdinalIgnoreCase) Then Return Back()
        If String.Equals(target, ActionTypes.PageFirst, StringComparison.OrdinalIgnoreCase) Then
            If _page Is _profile.Pages(0) Then Return False
            _history.Clear()
            _page = _profile.Pages(0)
            RaiseEvent Changed(Me, EventArgs.Empty)
            Return True
        End If

        Dim p = _profile.FindPage(target)
        If p Is Nothing OrElse p Is _page Then Return False
        _history.Push(_page.Name)
        _page = p
        RaiseEvent Changed(Me, EventArgs.Empty)
        Return True
    End Function

    ''' <summary>Zur vorigen Seite; ohne Verlauf zur ersten Seite.</summary>
    Public Function Back() As Boolean
        While _history.Count > 0
            Dim p = _profile.FindPage(_history.Pop())
            If p IsNot Nothing AndAlso p IsNot _page Then
                _page = p
                RaiseEvent Changed(Me, EventArgs.Empty)
                Return True
            End If
        End While
        If _page IsNot _profile.Pages(0) Then
            _page = _profile.Pages(0)
            RaiseEvent Changed(Me, EventArgs.Empty)
            Return True
        End If
        Return False
    End Function

    ''' <summary>
    ''' Automatische Profilwahl nach Prozessname des Vordergrundfensters (z. B. "obs64" oder "obs64.exe").
    ''' Passt kein Profil, gilt wieder das manuell gewählte. Liefert True bei einem Wechsel.
    ''' </summary>
    Public Function ApplyForegroundProcess(processName As String) As Boolean
        Dim match = FindAutoProfile(processName)
        Dim target = If(match, _config.FindProfile(_config.ActiveProfile))
        If target Is Nothing OrElse target Is _profile Then Return False
        SetProfile(target)
        Return True
    End Function

    Public Function FindAutoProfile(processName As String) As ProfileConfig
        Dim proc = NormalizeProcessName(processName)
        If proc.Length = 0 Then Return Nothing
        For Each p In _config.Profiles
            If String.IsNullOrWhiteSpace(p.AutoSwitchProcess) Then Continue For
            For Each candidate In p.AutoSwitchProcess.Split({";"c, ","c}, StringSplitOptions.RemoveEmptyEntries)
                If NormalizeProcessName(candidate) = proc Then Return p
            Next
        Next
        Return Nothing
    End Function

    Public Shared Function NormalizeProcessName(name As String) As String
        If name Is Nothing Then Return ""
        Dim s = name.Trim().ToLowerInvariant()
        If s.EndsWith(".exe", StringComparison.Ordinal) Then s = s.Substring(0, s.Length - 4)
        Return s
    End Function
End Class
