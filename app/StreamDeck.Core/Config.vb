' Datenmodell der Konfiguration (%APPDATA%\StreamDeck\config.json), Format siehe CLAUDE.md.
Imports System.Text.Json.Serialization

Public Class DeckConfig
    Public Property ActiveProfile As String = "Standard"
    ''' <summary>Ab dieser Haltedauer gilt ein Druck als lang (nur für Tasten mit longPress-Aktion).</summary>
    Public Property LongPressMs As Integer = 500
    Public Property Obs As New ObsSettings
    Public Property Profiles As New List(Of ProfileConfig)

    Public Function FindProfile(name As String) As ProfileConfig
        Return Profiles.FirstOrDefault(Function(p) String.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
    End Function
End Class

Public Class ObsSettings
    Public Property Url As String = "ws://localhost:4455"
    Public Property Password As String = ""
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
End Class

Public Class KeyConfig
    Public Property Label As String
    ''' <summary>Pfad zu einer Bilddatei (PNG, JPG, ICO …).</summary>
    Public Property Icon As String
    Public Property Press As ActionConfig
    Public Property LongPress As ActionConfig
End Class

''' <summary>
''' Eine Aktion. Welche Felder genutzt werden, hängt von <see cref="ActionType"/> ab:
''' hotkey: keys | text: text | media: key | sound: file, volume, device |
''' launch: path, args | url: url | command: command, shell, hidden |
''' obs: op, scene, source | page: target | multi: actions | wait: ms
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
    Public Property File As String
    ''' <summary>sound: 0–100</summary>
    Public Property Volume As Integer?
    ''' <summary>sound: Name (oder Teil davon) des Ausgabegeräts, leer = Standard</summary>
    Public Property Device As String
    ''' <summary>sound: höchstens so viele Sekunden spielen (am Ende kurz ausblenden); leer/0 = ganze Datei</summary>
    Public Property Duration As Double?
    Public Property Path As String
    Public Property Args As String
    Public Property Url As String
    Public Property Command As String
    ''' <summary>command: "cmd" (Standard) oder "powershell"</summary>
    Public Property Shell As String
    Public Property Hidden As Boolean?
    ''' <summary>obs: scene, startRecord, stopRecord, toggleRecord, startStream, stopStream, toggleStream, showSource, hideSource, toggleSource</summary>
    Public Property Op As String
    Public Property Scene As String
    Public Property Source As String
    ''' <summary>page: Seitenname, "@back" (vorige Seite) oder "@first" (erste Seite); profile: Profilname</summary>
    Public Property Target As String
    Public Property Actions As List(Of ActionConfig)
    ''' <summary>wait: Wartezeit in Millisekunden</summary>
    Public Property Ms As Integer?
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

    Public Shared ReadOnly All As String() = {Hotkey, Text, Media, Sound, Launch, Url, Command, Obs, Page, Profile, Multi, Wait}

    Public Shared ReadOnly ObsOps As String() = {"scene", "startRecord", "stopRecord", "toggleRecord",
        "startStream", "stopStream", "toggleStream", "showSource", "hideSource", "toggleSource"}

    Public Const PageBack As String = "@back"
    Public Const PageFirst As String = "@first"

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
            Case Url : Return $"Webseite {a.Url}"
            Case Command : Return $"{If(String.IsNullOrEmpty(a.Shell), "cmd", a.Shell)}: {Shorten(a.Command)}"
            Case Obs : Return $"OBS {a.Op} {If(a.Scene, "")} {If(a.Source, "")}".TrimEnd()
            Case Page : Return $"Seite → {a.Target}"
            Case Profile : Return $"Profil → {a.Target}"
            Case Multi : Return $"{If(a.Actions Is Nothing, 0, a.Actions.Count)} Aktionen nacheinander"
            Case Wait : Return $"{a.Ms} ms warten"
            Case Else : Return a.ActionType
        End Select
    End Function

    Private Shared Function Shorten(s As String) As String
        If s Is Nothing Then Return ""
        s = s.Replace(vbCrLf, " ").Replace(vbLf, " ")
        Return If(s.Length > 30, s.Substring(0, 30) & "…", s)
    End Function
End Class
