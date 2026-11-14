' Datenmodell der Konfiguration (%APPDATA%\StreamDeck\config.json), Format siehe CLAUDE.md.
Imports System.Text.Json.Serialization

Public Class DeckConfig
    Public Property ActiveProfile As String = "Standard"
    ''' <summary>Ab dieser Haltedauer gilt ein Druck als lang (nur für Tasten mit longPress-Aktion).</summary>
    Public Property LongPressMs As Integer = 500
    ''' <summary>Zeitfenster für den zweiten Druck eines Doppeldrucks (nur für Tasten mit doublePress-Aktion).</summary>
    Public Property DoublePressMs As Integer = 300
    ''' <summary>In Ordnern ist diese Taste (0–14) immer die Zurück-Taste.</summary>
    Public Property FolderBackKey As Integer = 0
    ''' <summary>Kurze Einblendung auf dem Bildschirm beim Wechsel von Profil/Seite/Ordner und für Meldungen.</summary>
    Public Property ShowOverlay As Boolean = True
    ''' <summary>App mit Administratorrechten starten (nötig für Hotkeys in Programmen, die als Administrator laufen).</summary>
    Public Property RunAsAdmin As Boolean
    Public Property Obs As New ObsSettings
    ''' <summary>Sounds zusätzlich ins Mikrofon mischen (z. B. damit man sie in Discord hört).</summary>
    Public Property VoiceMix As New VoiceMixSettings
    Public Property Profiles As New List(Of ProfileConfig)

    Public Function FindProfile(name As String) As ProfileConfig
        Return Profiles.FirstOrDefault(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
    End Function
End Class

Public Class ObsSettings
    Public Property Url As String = "ws://localhost:4455"
    Public Property Password As String = ""
End Class

''' <summary>
''' Sprach-Mix: Die App nimmt das Mikrofon auf, mischt die Sounds dazu und gibt beides auf einem
''' virtuellen Audiokabel aus (z. B. VB-Audio Virtual Cable). In Discord &amp; Co. wird dann
''' "CABLE Output" als Eingabegerät gewählt.
''' </summary>
Public Class VoiceMixSettings
    Public Property Enabled As Boolean
    ''' <summary>Name (oder Teil davon) des Mikrofons, leer = Standard-Aufnahmegerät für Kommunikation.</summary>
    Public Property Microphone As String
    ''' <summary>Name (oder Teil davon) des Ausgabegeräts des virtuellen Kabels.</summary>
    Public Property Output As String = "CABLE Input"
    ''' <summary>Lautstärke des Mikrofons im Mix in %, 0–400 (über 100 = verstärken).</summary>
    Public Property MicVolume As Integer = 100
    ''' <summary>Rauschsperre: Mikrofon zwischen dem Sprechen ausblenden (wirkt nur aufs Mikrofon, nicht auf die Sounds).</summary>
    Public Property NoiseGate As Boolean = True
    ''' <summary>Schwelle der Rauschsperre in dBFS (-80 … -10); leiser gilt als Rauschen.</summary>
    Public Property NoiseGateDb As Integer = -45

    Public Const MaxMicVolume As Integer = 400
End Class

Public Class ProfileConfig
    Public Property Name As String = "Standard"
    ''' <summary>Prozessname(n) des Vordergrundfensters, z. B. "obs64" oder "chrome;msedge". Nothing = kein Automatismus.</summary>
    Public Property AutoSwitchProcess As String
    Public Property Pages As New List(Of PageConfig)

    Public Function FindPage(name As String) As PageConfig
        Return Pages.FirstOrDefault(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
    End Function
End Class

Public Class PageConfig
    Public Property Name As String = "Start"
    ''' <summary>
    ''' True = Ordner: wird über eine Taste geöffnet (verschachtelbar), hat eine feste Zurück-Taste
    ''' (DeckConfig.FolderBackKey) und wird beim Vor-/Zurückblättern übersprungen.
    ''' </summary>
    Public Property Folder As Boolean?
    ''' <summary>Schlüssel "0".."14" (Taste zeilenweise von oben links).</summary>
    Public Property Keys As New Dictionary(Of String, KeyConfig)

    Public Function GetKey(index As Integer) As KeyConfig
        Dim k As KeyConfig = Nothing
        If Keys IsNot Nothing AndAlso Keys.TryGetValue(index.ToString(Globalization.CultureInfo.InvariantCulture), k) Then Return k
        Return Nothing
    End Function

    Public Sub SetKey(index As Integer, key As KeyConfig)
        Dim id = index.ToString(Globalization.CultureInfo.InvariantCulture)
        If key Is Nothing Then Keys.Remove(id) Else Keys(id) = key
    End Sub

    <JsonIgnore>
    Public ReadOnly Property IsFolder As Boolean
        Get
            Return Folder.GetValueOrDefault()
        End Get
    End Property
End Class

Public Class KeyConfig
    Public Property Label As String
    ''' <summary>Pfad zu einer Bilddatei (PNG, JPG, ICO …).</summary>
    Public Property Icon As String
    ''' <summary>Freitext-Beschreibung (nur für Oberfläche und Druckvorlage).</summary>
    Public Property Description As String
    Public Property Press As ActionConfig
    Public Property LongPress As ActionConfig
    ''' <summary>Aktion bei zweimal schnell hintereinander drücken.</summary>
    Public Property DoublePress As ActionConfig
    ''' <summary>Aktion beim Loslassen (z. B. Push-to-Talk: Druck = Mikro an, Loslassen = Mikro aus).</summary>
    Public Property Release As ActionConfig
    ''' <summary>Kurzen Druck wiederholen, solange die Taste gehalten wird (z. B. Lautstärke). Nur ohne lang/doppelt.</summary>
    Public Property Repeat As Boolean?
End Class

''' <summary>
''' Eine Aktion. Welche Felder genutzt werden, hängt von <see cref="ActionType"/> ab:
''' hotkey: keys | text: text, paste, enter | media: key | sound: file, volume, device, duration, restart |
''' launch: path, args | close: path, force | url: url, browser | http: url, method, body, contentType, headers |
''' command: command, shell, hidden | mic: op, device | timer: seconds, file, volume, device |
''' obs: op, scene, source | page: target | multi: actions | toggle: actions | wait: ms
''' </summary>
Public Class ActionConfig
    <JsonPropertyName("type")>
    Public Property ActionType As String

    ''' <summary>media: playpause, next, prev, stop, mute, volup, voldown</summary>
    <JsonPropertyName("key")>
    Public Property MediaKey As String

    ''' <summary>hotkey: z. B. "Ctrl+Shift+M" oder "F13"</summary>
    Public Property Keys As String
    Public Property Text As String
    ''' <summary>text: True = über Zwischenablage + Strg+V einfügen, sonst Zeichen für Zeichen tippen</summary>
    Public Property Paste As Boolean?
    ''' <summary>text: danach Enter drücken</summary>
    Public Property Enter As Boolean?
    Public Property File As String
    ''' <summary>sound: 0–100</summary>
    Public Property Volume As Integer?
    ''' <summary>sound: Name (oder Teil davon) des Ausgabegeräts, leer = Standard</summary>
    Public Property Device As String
    ''' <summary>sound: höchstens so viele Sekunden spielen (am Ende kurz ausblenden); leer/0 = ganze Datei</summary>
    Public Property Duration As Double?
    ''' <summary>sound: True = erneuter Druck startet von vorn, sonst stoppt er den laufenden Sound</summary>
    Public Property Restart As Boolean?
    ''' <summary>sound: False = nicht in den Sprach-Mix (Discord) mischen; leer = mischen, wenn der Sprach-Mix an ist</summary>
    Public Property Voice As Boolean?
    Public Property Path As String
    Public Property Args As String
    Public Property Url As String
    ''' <summary>url: Browser-Programm (z. B. "firefox.exe" oder voller Pfad), leer = Standardbrowser</summary>
    Public Property Browser As String
    ''' <summary>http: GET (Standard), POST, PUT, PATCH, DELETE</summary>
    Public Property Method As String
    ''' <summary>http: Inhalt der Anfrage (bei POST/PUT/PATCH)</summary>
    Public Property Body As String
    ''' <summary>http: Inhaltstyp des Body, Standard application/json</summary>
    Public Property ContentType As String
    ''' <summary>http: zusätzliche Kopfzeilen, z. B. Authorization</summary>
    Public Property Headers As Dictionary(Of String, String)
    ''' <summary>close: True = Prozess sofort beenden (Kill), sonst Fenster höflich schließen</summary>
    Public Property Force As Boolean?
    ''' <summary>timer: Dauer in Sekunden</summary>
    Public Property Seconds As Double?
    Public Property Command As String
    ''' <summary>command: "cmd" (Standard) oder "powershell"</summary>
    Public Property Shell As String
    Public Property Hidden As Boolean?
    ''' <summary>mic: toggle (Standard), mute, unmute |
    ''' obs: scene, startRecord, stopRecord, toggleRecord, startStream, stopStream, toggleStream, showSource, hideSource, toggleSource</summary>
    Public Property Op As String
    Public Property Scene As String
    Public Property Source As String
    ''' <summary>page: Seitenname, "@back" (vorige Seite) oder "@first" (erste Seite); profile: Profilname</summary>
    Public Property Target As String
    Public Property Actions As List(Of ActionConfig)
    ''' <summary>multi: True = alle Aktionen gleichzeitig starten statt nacheinander</summary>
    Public Property Parallel As Boolean?
    ''' <summary>wait: Wartezeit in Millisekunden</summary>
    Public Property Ms As Integer?
    ''' <summary>window: Zielmonitor (1 = erster Monitor laut Windows-Reihenfolge)</summary>
    Public Property Monitor As Integer?
    ''' <summary>window (op "move"): Position und Größe in Prozent des Monitor-Arbeitsbereichs</summary>
    Public Property X As Integer?
    Public Property Y As Integer?
    Public Property Width As Integer?
    Public Property Height As Integer?
    ''' <summary>plugin: Name des Plugins (DLL-Plugin oder Skript im Plugin-Ordner)</summary>
    Public Property Plugin As String
End Class

Public NotInheritable Class ActionTypes
    Private Sub New()
    End Sub

    Public Const Hotkey As String = "hotkey"
    Public Const Text As String = "text"
    Public Const Media As String = "media"
    Public Const Sound As String = "sound"
    Public Const Launch As String = "launch"
    Public Const Url As String = "url"
    Public Const Command As String = "command"
    Public Const Obs As String = "obs"
    Public Const Page As String = "page"
    Public Const Profile As String = "profile"
    Public Const Multi As String = "multi"
    Public Const Wait As String = "wait"
    Public Const Close As String = "close"
    Public Const Http As String = "http"
    Public Const Mic As String = "mic"
    Public Const Timer As String = "timer"
    Public Const Toggle As String = "toggle"
    Public Const Random As String = "random"
    Public Const Window As String = "window"
    Public Const Plugin As String = "plugin"

    Public Shared ReadOnly All As String() = {Hotkey, Text, Media, Sound, Launch, Close, Url, Http, Command, Mic, Timer, Window, Obs, Page, Profile, Multi, Toggle, Random, Plugin, Wait}

    ''' <summary>window: Monitor wechseln, maximieren, minimieren, wiederherstellen, Position/Größe in Prozent</summary>
    Public Shared ReadOnly WindowOps As String() = {"monitor", "nextMonitor", "maximize", "minimize", "restore", "move"}

    Public Shared ReadOnly HttpMethods As String() = {"GET", "POST", "PUT", "PATCH", "DELETE"}
    Public Shared ReadOnly MicOps As String() = {"toggle", "mute", "unmute"}

    Public Shared ReadOnly ObsOps As String() = {"scene", "startRecord", "stopRecord", "toggleRecord",
        "startStream", "stopStream", "toggleStream", "showSource", "hideSource", "toggleSource"}

    Public Const PageBack As String = "@back"
    Public Const PageFirst As String = "@first"
    ''' <summary>page/profile: nächste bzw. vorige Seite (Ordner werden übersprungen) oder nächstes/voriges Profil</summary>
    Public Const TargetNext As String = "@next"
    Public Const TargetPrev As String = "@prev"

    Private Shared Function DescribeTarget(target As String) As String
        Select Case If(target, "").ToLowerInvariant()
            Case PageBack : Return "zurück"
            Case PageFirst : Return "erste Seite"
            Case TargetNext : Return "nächste"
            Case TargetPrev : Return "vorige"
            Case Else : Return target
        End Select
    End Function

    ''' <summary>Kurzbeschreibung für Tooltips und Log.</summary>
    Public Shared Function Describe(a As ActionConfig) As String
        If a Is Nothing OrElse String.IsNullOrEmpty(a.ActionType) Then Return "(keine Aktion)"
        Select Case a.ActionType.ToLowerInvariant()
            Case Hotkey : Return $"Tastenkombination {a.Keys}"
            Case Text : Return $"Text '{Shorten(a.Text)}'"
            Case Media : Return $"Medientaste {a.MediaKey}"
            Case Sound
                Dim s = $"Sound {IO.Path.GetFileName(If(a.File, ""))}"
                If a.Duration.GetValueOrDefault() > 0 Then s &= $" ({a.Duration.Value.ToString("0.#", Globalization.CultureInfo.CurrentCulture)} s)"
                Return s
            Case Launch : Return $"Starten {a.Path} {a.Args}".TrimEnd()
            Case Close : Return $"{If(a.Force.GetValueOrDefault(), "Beenden (erzwungen)", "Schließen")} {a.Path}".TrimEnd()
            Case Url : Return $"Webseite {a.Url}" & If(String.IsNullOrWhiteSpace(a.Browser), "", $" in {a.Browser}")
            Case Http : Return $"HTTP {If(String.IsNullOrWhiteSpace(a.Method), "GET", a.Method.Trim().ToUpperInvariant())} {a.Url}"
            Case Mic
                Select Case If(a.Op, "toggle").ToLowerInvariant()
                    Case "mute" : Return "Mikrofon stumm"
                    Case "unmute" : Return "Mikrofon an"
                    Case Else : Return "Mikrofon stumm/an"
                End Select
            Case Timer : Return $"Timer {FormatSeconds(a.Seconds.GetValueOrDefault())}"
            Case Toggle : Return $"Umschalten zwischen {If(a.Actions Is Nothing, 0, a.Actions.Count)} Aktionen"
            Case Command : Return $"{If(String.IsNullOrEmpty(a.Shell), "cmd", a.Shell)}: {Shorten(a.Command)}"
            Case Obs : Return $"OBS {a.Op} {If(a.Scene, "")} {If(a.Source, "")}".TrimEnd()
            Case Page : Return $"Seite → {DescribeTarget(a.Target)}"
            Case Profile : Return $"Profil → {DescribeTarget(a.Target)}"
            Case Multi : Return $"{If(a.Actions Is Nothing, 0, a.Actions.Count)} Aktionen {If(a.Parallel.GetValueOrDefault(), "gleichzeitig", "nacheinander")}"
            Case Random : Return $"Zufällig eine von {If(a.Actions Is Nothing, 0, a.Actions.Count)} Aktionen"
            Case Window
                Dim target = If(String.IsNullOrWhiteSpace(a.Path), "aktives Fenster", a.Path)
                Select Case If(a.Op, "").ToLowerInvariant()
                    Case "monitor" : Return $"{target} → Monitor {a.Monitor.GetValueOrDefault(1)}"
                    Case "nextmonitor" : Return $"{target} → nächster Monitor"
                    Case "maximize" : Return $"{target} maximieren"
                    Case "minimize" : Return $"{target} minimieren"
                    Case "restore" : Return $"{target} wiederherstellen"
                    Case "move" : Return $"{target} → {a.X}/{a.Y} % · {a.Width}×{a.Height} %"
                    Case Else : Return $"Fenster {a.Op}"
                End Select
            Case Plugin : Return $"Plugin {a.Plugin} {a.Args}".TrimEnd()
            Case Wait : Return $"{a.Ms} ms warten"
            Case Else : Return a.ActionType
        End Select
    End Function

    ''' <summary>20 -> "20 s", 90 -> "1:30", 3725 -> "1:02:05"</summary>
    Public Shared Function FormatSeconds(seconds As Double) As String
        Dim t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)))
        If t.TotalHours >= 1 Then Return $"{CInt(Math.Floor(t.TotalHours))}:{t.Minutes:00}:{t.Seconds:00}"
        If t.TotalMinutes >= 1 Then Return $"{t.Minutes}:{t.Seconds:00}"
        Return $"{t.Seconds} s"
    End Function

    Private Shared Function Shorten(s As String) As String
        If s Is Nothing Then Return ""
        s = s.Replace(vbCrLf, " ").Replace(vbLf, " ")
        Return If(s.Length > 30, s.Substring(0, 30) & "…", s)
    End Function
End Class
