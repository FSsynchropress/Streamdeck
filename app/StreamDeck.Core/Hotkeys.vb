' Umsetzung von Tastennamen ("Ctrl+Shift+M", "F13", "playpause") in Windows-Virtual-Key-Codes.

Public Module Hotkeys
    Private ReadOnly Names As New Dictionary(Of String, UShort)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly MediaNames As New Dictionary(Of String, UShort)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly Modifiers As New HashSet(Of UShort) From {&H10US, &H11US, &H12US, &H5BUS}
    Private ReadOnly ExtendedKeys As New HashSet(Of UShort) From {
        &H21US, &H22US, &H23US, &H24US, &H25US, &H26US, &H27US, &H28US, &H2CUS, &H2DUS, &H2EUS,
        &H5BUS, &H5CUS, &H5DUS, &H6FUS, &H90US,
        &HADUS, &HAEUS, &HAFUS, &HB0US, &HB1US, &HB2US, &HB3US}

    Sub New()
        Add(&H11, "Ctrl", "Strg", "Control")
        Add(&H10, "Shift", "Umschalt", "Umsch")
        Add(&H12, "Alt")
        Add(&H5B, "Win", "Windows", "Meta")
        Add(&HD, "Enter", "Return", "Eingabe")
        Add(&H1B, "Esc", "Escape")
        Add(&H9, "Tab")
        Add(&H20, "Space", "Leertaste")
        Add(&H8, "Backspace", "Rück")
        Add(&H2E, "Delete", "Del", "Entf")
        Add(&H2D, "Insert", "Ins", "Einfg")
        Add(&H24, "Home", "Pos1")
        Add(&H23, "End", "Ende")
        Add(&H21, "PageUp", "PgUp", "BildAuf")
        Add(&H22, "PageDown", "PgDn", "BildAb")
        Add(&H25, "Left", "Links")
        Add(&H26, "Up", "Hoch")
        Add(&H27, "Right", "Rechts")
        Add(&H28, "Down", "Runter")
        Add(&H2C, "PrintScreen", "PrtSc", "Druck")
        Add(&H13, "Pause")
        Add(&H14, "CapsLock")
        Add(&H90, "NumLock")
        Add(&H91, "ScrollLock")
        Add(&H5D, "Apps", "Menu", "Kontextmenü")
        Add(&H6A, "Multiply", "NumMultiply")
        Add(&H6B, "Add", "NumAdd", "NumPlus")
        Add(&H6D, "Subtract", "NumSubtract", "NumMinus")
        Add(&H6E, "Decimal", "NumDecimal")
        Add(&H6F, "Divide", "NumDivide")
        Add(&HBB, "Plus")
        Add(&HBD, "Minus")
        Add(&HBC, "Comma", "Komma")
        Add(&HBE, "Period", "Punkt")
        For i = 1 To 24
            Add(CUShort(&H6F + i), "F" & i)
        Next
        For i = 0 To 9
            Add(CUShort(&H30 + i), i.ToString())
            Add(CUShort(&H60 + i), "Num" & i)
        Next
        For c = AscW("A"c) To AscW("Z"c)
            Add(CUShort(c), ChrW(c).ToString())
        Next

        MediaNames("playpause") = &HB3
        MediaNames("play") = &HB3
        MediaNames("next") = &HB0
        MediaNames("prev") = &HB1
        MediaNames("previous") = &HB1
        MediaNames("stop") = &HB2
        MediaNames("mute") = &HAD
        MediaNames("voldown") = &HAE
        MediaNames("volup") = &HAF
    End Sub

    Private Sub Add(vk As Integer, ParamArray aliases As String())
        For Each a In aliases
            Names(a) = CUShort(vk)
        Next
    End Sub

    Public ReadOnly MediaKeyNames As String() = {"playpause", "next", "prev", "stop", "mute", "volup", "voldown"}

    ''' <summary>"Ctrl+Shift+M" -> {VK_CONTROL, VK_SHIFT, 'M'}. Wirft FormatException mit verständlicher Meldung.</summary>
    Public Function ParseHotkey(text As String) As List(Of UShort)
        If String.IsNullOrWhiteSpace(text) Then Throw New FormatException("Keine Tastenkombination angegeben.")
        Dim result As New List(Of UShort)
        For Each raw In text.Split("+"c)
            Dim name = raw.Trim()
            If name.Length = 0 Then Throw New FormatException("Leerer Tastenname – für die Plus-Taste 'Plus' schreiben.")
            Dim vk As UShort
            If Not Names.TryGetValue(name, vk) Then Throw New FormatException($"Unbekannte Taste '{name}'.")
            If result.Contains(vk) Then Throw New FormatException($"Taste '{name}' doppelt.")
            result.Add(vk)
        Next
        ' Modifier zuerst drücken
        Return result.OrderBy(Function(v) If(Modifiers.Contains(v), 0, 1)).ToList()
    End Function

    Public Function TryParseHotkey(text As String, ByRef keys As List(Of UShort), ByRef errorMessage As String) As Boolean
        Try
            keys = ParseHotkey(text)
            errorMessage = Nothing
            Return True
        Catch ex As FormatException
            keys = Nothing
            errorMessage = ex.Message
            Return False
        End Try
    End Function

    Public Function MediaKeyCode(name As String) As UShort
        Dim vk As UShort
        If name IsNot Nothing AndAlso MediaNames.TryGetValue(name.Trim(), vk) Then Return vk
        Throw New FormatException($"Unbekannte Medientaste '{name}' (erlaubt: {String.Join(", ", MediaKeyNames)}).")
    End Function

    Public Function IsExtendedKey(vk As UShort) As Boolean
        Return ExtendedKeys.Contains(vk)
    End Function

    Public Function IsKnownKeyName(name As String) As Boolean
        Return Names.ContainsKey(name)
    End Function
End Module
