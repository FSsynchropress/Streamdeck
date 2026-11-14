' Installation und Updates über Velopack. Neue Versionen kommen als GitHub-Release
' (gebaut von .github/workflows/release.yml), die App prüft im Hintergrund darauf.
Imports System.Threading
Imports Velopack
Imports Velopack.Sources

Public Class Updater
    ''' <summary>Öffentliches GitHub-Repo, dessen Releases die Updates enthalten.</summary>
    Public Const RepoUrl As String = "https://github.com/FSsynchropress/Streamdeck"

    Private Shared ReadOnly FirstCheckDelay As TimeSpan = TimeSpan.FromSeconds(15)
    Private Shared ReadOnly CheckInterval As TimeSpan = TimeSpan.FromHours(6)

    Private ReadOnly _manager As UpdateManager
    Private ReadOnly _log As Action(Of String)
    Private ReadOnly _busy As New SemaphoreSlim(1, 1)
    Private _timer As System.Threading.Timer
    Private _ready As UpdateInfo

    ''' <summary>Ein Update wurde heruntergeladen und kann mit Neustart installiert werden (Hintergrund-Thread).</summary>
    Public Event UpdateReady(version As String)

    Public Sub New(log As Action(Of String))
        _log = log
        Try
            _manager = New UpdateManager(New GithubSource(RepoUrl, Nothing, False))
        Catch ex As Exception
            _log($"Updates nicht verfügbar: {ex.Message}")
        End Try
    End Sub

    ''' <summary>True, wenn die App über das Setup installiert wurde (sonst z. B. Start aus Visual Studio).</summary>
    Public ReadOnly Property IsInstalled As Boolean
        Get
            Return _manager IsNot Nothing AndAlso _manager.IsInstalled
        End Get
    End Property

    ''' <summary>Laufende Version, z. B. "1.2.0".</summary>
    Public ReadOnly Property CurrentVersion As String
        Get
            If IsInstalled Then Return _manager.CurrentVersion.ToString()
            Dim v = GetType(Updater).Assembly.GetName().Version
            Return $"{v.Major}.{v.Minor}.{v.Build} (nicht installiert)"
        End Get
    End Property

    ''' <summary>Heruntergeladene, noch nicht installierte Version oder Nothing.</summary>
    Public ReadOnly Property ReadyVersion As String
        Get
            Return _ready?.TargetFullRelease.Version.ToString()
        End Get
    End Property

    ''' <summary>Startet die regelmäßige Prüfung im Hintergrund.</summary>
    Public Sub Start()
        If Not IsInstalled Then Return
        _timer = New System.Threading.Timer(Sub(s) BackgroundCheck(), Nothing, FirstCheckDelay, CheckInterval)
    End Sub

    Private Async Sub BackgroundCheck()
        Await CheckAsync(manual:=False)   ' fängt alle Fehler selbst ab
    End Sub

    ''' <summary>Sucht nach einem Update und lädt es herunter. Gibt eine Meldung für den Benutzer zurück.</summary>
    Public Async Function CheckAsync(manual As Boolean) As Task(Of String)
        If Not IsInstalled Then Return "Updates gibt es nur in der installierten Version (Setup von GitHub)."
        If Not Await _busy.WaitAsync(0) Then Return "Die Update-Suche läuft bereits."
        Try
            If _ready IsNot Nothing Then Return $"Version {ReadyVersion} ist bereit und wird beim Neustart installiert."
            Dim info = Await _manager.CheckForUpdatesAsync()
            If info Is Nothing Then
                If manual Then _log($"Keine neue Version (installiert: {CurrentVersion})")
                Return $"Du hast die neueste Version ({CurrentVersion})."
            End If
            Dim target = info.TargetFullRelease.Version.ToString()
            _log($"Update auf {target} wird heruntergeladen …")
            Await _manager.DownloadUpdatesAsync(info)
            _ready = info
            _log($"Update auf {target} bereit")
            RaiseEvent UpdateReady(target)
            Return $"Version {target} wurde heruntergeladen."
        Catch ex As Exception
            _log($"Update-Suche fehlgeschlagen: {ex.Message}")
            Return $"Die Update-Suche ist fehlgeschlagen:{vbCrLf}{ex.Message}"
        Finally
            _busy.Release()
        End Try
    End Function

    ''' <summary>Installiert das heruntergeladene Update und startet die App neu (beendet den Prozess).</summary>
    Public Sub ApplyAndRestart()
        If _ready Is Nothing Then Return
        [Stop]()
        _manager.ApplyUpdatesAndRestart(_ready.TargetFullRelease, {"--tray"})
    End Sub

    Public Sub [Stop]()
        _timer?.Dispose()
        _timer = Nothing
    End Sub
End Class
