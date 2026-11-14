' Dialoge: Taste bearbeiten, Aktion bearbeiten (auch Schritte von Mehrfach-Aktionen), Einstellungen.
' Hinweis: In VB.NET gelten auch typografische Anführungszeichen als String-Begrenzer – in Texten daher nur ' verwenden.
Imports System.Runtime.InteropServices
Imports StreamDeck.Core

''' <summary>Eintrag für Auswahllisten: interner Wert + Anzeigetext.</summary>
Friend Class Choice
    Public ReadOnly Property Value As String
    Public ReadOnly Property Text As String

    Public Sub New(value As String, text As String)
        Me.Value = value
        Me.Text = text
    End Sub

    Public Overrides Function ToString() As String
        Return Text
    End Function

    Public Shared Sub Fill(combo As ComboBox, items As IEnumerable(Of Choice), selected As String)
        combo.Items.Clear()
        For Each c In items
            combo.Items.Add(c)
        Next
        Dim hit = combo.Items.Cast(Of Choice).FirstOrDefault(Function(c) String.Equals(c.Value, selected, StringComparison.OrdinalIgnoreCase))
        If hit IsNot Nothing Then
            combo.SelectedItem = hit
        ElseIf combo.DropDownStyle <> ComboBoxStyle.DropDownList AndAlso selected IsNot Nothing Then
            combo.Text = selected
        ElseIf combo.Items.Count > 0 Then
            combo.SelectedIndex = 0
        End If
    End Sub

    ''' <summary>Gewählter Wert; bei editierbaren Listen auch frei eingegebener Text.</summary>
    Public Shared Function GetValue(combo As ComboBox) As String
        Dim c = TryCast(combo.SelectedItem, Choice)
        If c IsNot Nothing AndAlso (combo.DropDownStyle = ComboBoxStyle.DropDownList OrElse combo.Text = c.Text) Then Return c.Value
        If combo.DropDownStyle = ComboBoxStyle.DropDownList Then Return Nothing
        ' frei eingegebener Text, der einem Anzeigetext entspricht
        Dim byText = combo.Items.Cast(Of Choice).FirstOrDefault(Function(x) x.Text = combo.Text)
        If byText IsNot Nothing Then Return byText.Value
        Return If(String.IsNullOrWhiteSpace(combo.Text), Nothing, combo.Text.Trim())
    End Function
End Class

' ====================================================================== Aktionseditor

Public Class ActionEditor
    Inherits UserControl

    Private Shared ReadOnly TypeChoices As Choice() = {
        New Choice("", "(keine Aktion)"),
        New Choice(ActionTypes.Hotkey, "Tastenkombination senden"),
        New Choice(ActionTypes.Text, "Text einfügen"),
        New Choice(ActionTypes.Media, "Medientaste"),
        New Choice(ActionTypes.Sound, "Sound abspielen"),
        New Choice(ActionTypes.Launch, "Programm / Datei / Ordner öffnen"),
        New Choice(ActionTypes.Url, "Webseite öffnen"),
        New Choice(ActionTypes.Command, "Befehl / Skript ausführen"),
        New Choice(ActionTypes.Obs, "OBS steuern"),
        New Choice(ActionTypes.Page, "Seite wechseln"),
        New Choice(ActionTypes.Profile, "Profil wechseln"),
        New Choice(ActionTypes.Multi, "Mehrere Aktionen nacheinander"),
        New Choice(ActionTypes.Wait, "Warten (für Mehrfach-Aktionen)")}

    Private Shared ReadOnly MediaChoices As Choice() = {
        New Choice("playpause", "Play / Pause"), New Choice("next", "Nächster Titel"), New Choice("prev", "Vorheriger Titel"),
        New Choice("stop", "Stopp"), New Choice("mute", "Stumm (Mute)"), New Choice("volup", "Lauter"), New Choice("voldown", "Leiser")}

    Private Shared ReadOnly ObsChoices As Choice() = {
        New Choice("scene", "Szene wechseln"),
        New Choice("toggleRecord", "Aufnahme starten/stoppen"), New Choice("startRecord", "Aufnahme starten"), New Choice("stopRecord", "Aufnahme stoppen"),
        New Choice("toggleStream", "Stream starten/stoppen"), New Choice("startStream", "Stream starten"), New Choice("stopStream", "Stream stoppen"),
        New Choice("toggleSource", "Quelle ein-/ausblenden"), New Choice("showSource", "Quelle einblenden"), New Choice("hideSource", "Quelle ausblenden")}

    Private ReadOnly _runner As ActionRunner
    Private ReadOnly _pageNames As IList(Of String)
    Private ReadOnly _profileNames As IList(Of String)
    Private ReadOnly _typeCombo As New ComboBox With {.DropDownStyle = ComboBoxStyle.DropDownList, .Anchor = AnchorStyles.Left Or AnchorStyles.Right, .Margin = New Padding(0, 0, 6, 0)}
    Private ReadOnly _fields As New TableLayoutPanel With {.ColumnCount = 2, .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Dock = DockStyle.Top}
    Private ReadOnly _testButton As New ThemedButton("Testen", ButtonKind.Secondary, Theme.GlyphPlay) With {.BaseHeight = 32, .Margin = Padding.Empty}
    Private ReadOnly _hints As New List(Of Label)
    Private ReadOnly _inputs As New Dictionary(Of String, Control)
    Private _working As New ActionConfig
    Private _steps As New List(Of ActionConfig)
    Private _stepList As ListBox
    Private _shownType As String = ""
    Private _loading As Boolean

    Public Sub New(runner As ActionRunner, pageNames As IList(Of String), profileNames As IList(Of String))
        _runner = runner
        _pageNames = pageNames
        _profileNames = profileNames
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        ForeColor = Theme.Fg

        _fields.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        _fields.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))

        Dim top As New TableLayoutPanel With {.Dock = DockStyle.Top, .AutoSize = True, .ColumnCount = 2}
        top.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        top.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Dim typeLabel = Theme.FieldLabel("Aktion")
        typeLabel.Padding = New Padding(0, 0, 0, 4)
        top.Controls.Add(typeLabel, 0, 0)
        top.SetColumnSpan(typeLabel, 2)
        top.Controls.Add(_typeCombo, 0, 1)
        top.Controls.Add(_testButton, 1, 1)
        AddHandler _testButton.Click, AddressOf TestClicked

        Controls.Add(_fields)
        Controls.Add(top)
        For Each c In TypeChoices
            _typeCombo.Items.Add(c)
        Next
        Theme.Apply(Me)
        AddHandler _typeCombo.SelectedIndexChanged, AddressOf TypeChanged
        AddHandler SizeChanged, Sub() FitHints()
        LoadAction(Nothing)
    End Sub

    ''' <summary>Eigener Testen-Knopf (im Inspektor gibt es ihn in der Fußleiste).</summary>
    Public Property ShowTestButton As Boolean
        Get
            Return _testButton.Visible
        End Get
        Set(value As Boolean)
            _testButton.Visible = value
        End Set
    End Property

    Private Sub FitHints()
        Dim w = Math.Max(120, Width - 4)
        For Each l In _hints
            l.MaximumSize = New Size(w, 0)
        Next
    End Sub

    ' ------------------------------------------------------------------ Laden / Lesen

    Public Sub LoadAction(a As ActionConfig)
        _working = If(ConfigStore.CloneAction(a), New ActionConfig With {.ActionType = ""})
        _steps = If(_working.Actions IsNot Nothing, _working.Actions.Select(Function(x) ConfigStore.CloneAction(x)).ToList(), New List(Of ActionConfig))
        _loading = True
        Dim t = If(_working.ActionType, "")
        _typeCombo.SelectedItem = If(TypeChoices.FirstOrDefault(Function(c) c.Value.Equals(t, StringComparison.OrdinalIgnoreCase)), TypeChoices(0))
        _loading = False
        BuildFields()
    End Sub

    Private ReadOnly Property SelectedType As String
        Get
            Return If(TryCast(_typeCombo.SelectedItem, Choice)?.Value, "")
        End Get
    End Property

    ''' <summary>Die bearbeitete Aktion (nur mit den Feldern des gewählten Typs) oder Nothing.</summary>
    Public ReadOnly Property Value As ActionConfig
        Get
            Collect()
            Return Clean(_working)
        End Get
    End Property

    ''' <summary>Prüft die Eingaben. Nothing = in Ordnung, sonst Fehlermeldung.</summary>
    Public Function ValidateInput() As String
        Dim a = Value
        If a Is Nothing Then Return Nothing
        Select Case a.ActionType
            Case ActionTypes.Hotkey
                Dim keys As List(Of UShort) = Nothing
                Dim err As String = Nothing
                If Not Hotkeys.TryParseHotkey(a.Keys, keys, err) Then Return err
            Case ActionTypes.Text : If String.IsNullOrEmpty(a.Text) Then Return "Bitte einen Text eingeben."
            Case ActionTypes.Sound : If String.IsNullOrWhiteSpace(a.File) Then Return "Bitte eine Sounddatei wählen."
            Case ActionTypes.Launch : If String.IsNullOrWhiteSpace(a.Path) Then Return "Bitte ein Programm, eine Datei oder einen Ordner angeben."
            Case ActionTypes.Url
                Try
                    ProcessActions.NormalizeUrl(a.Url)
                Catch ex As ArgumentException
                    Return ex.Message
                End Try
            Case ActionTypes.Command : If String.IsNullOrWhiteSpace(a.Command) Then Return "Bitte einen Befehl eingeben."
            Case ActionTypes.Obs
                Dim op = If(a.Op, "")
                If op = "scene" AndAlso String.IsNullOrWhiteSpace(a.Scene) Then Return "Bitte den Namen der Szene angeben."
                If op.EndsWith("Source", StringComparison.Ordinal) AndAlso String.IsNullOrWhiteSpace(a.Source) Then Return "Bitte den Namen der Quelle angeben."
            Case ActionTypes.Page, ActionTypes.Profile : If String.IsNullOrWhiteSpace(a.Target) Then Return "Bitte ein Ziel wählen."
            Case ActionTypes.Multi : If a.Actions Is Nothing OrElse a.Actions.Count = 0 Then Return "Bitte mindestens einen Schritt hinzufügen."
        End Select
        Return Nothing
    End Function

    Private Shared Function Clean(a As ActionConfig) As ActionConfig
        If a Is Nothing OrElse String.IsNullOrEmpty(a.ActionType) Then Return Nothing
        Dim r As New ActionConfig With {.ActionType = a.ActionType}
        Select Case a.ActionType
            Case ActionTypes.Hotkey : r.Keys = a.Keys
            Case ActionTypes.Text
                r.Text = a.Text
                If a.Paste.GetValueOrDefault() Then r.Paste = True
            Case ActionTypes.Media : r.MediaKey = a.MediaKey
            Case ActionTypes.Sound : r.File = a.File : r.Volume = a.Volume : r.Device = a.Device : r.Duration = a.Duration
            Case ActionTypes.Launch : r.Path = a.Path : r.Args = a.Args
            Case ActionTypes.Url : r.Url = a.Url
            Case ActionTypes.Command
                r.Command = a.Command
                r.Shell = a.Shell
                If a.Hidden.GetValueOrDefault() Then r.Hidden = True
            Case ActionTypes.Obs : r.Op = a.Op : r.Scene = a.Scene : r.Source = a.Source
            Case ActionTypes.Page, ActionTypes.Profile : r.Target = a.Target
            Case ActionTypes.Multi : r.Actions = a.Actions
            Case ActionTypes.Wait : r.Ms = a.Ms
        End Select
        Return r
    End Function

    ''' <summary>Überträgt die gerade angezeigten Eingabefelder in _working.</summary>
    Private Sub Collect()
        Dim t = _shownType
        _working.ActionType = t
        Select Case t
            Case ActionTypes.Hotkey : _working.Keys = TextOf("keys")
            Case ActionTypes.Text
                _working.Text = DirectCast(_inputs("text"), TextBox).Text
                _working.Paste = DirectCast(_inputs("paste"), CheckBox).Checked
            Case ActionTypes.Media : _working.MediaKey = Choice.GetValue(DirectCast(_inputs("media"), ComboBox))
            Case ActionTypes.Sound
                _working.File = TextOf("file")
                _working.Volume = CInt(DirectCast(_inputs("volume"), NumericUpDown).Value)
                Dim dur = CDbl(DirectCast(_inputs("duration"), NumericUpDown).Value)
                _working.Duration = If(dur > 0, dur, CType(Nothing, Double?))
                Dim dev = Choice.GetValue(DirectCast(_inputs("device"), ComboBox))
                _working.Device = If(String.IsNullOrEmpty(dev), Nothing, dev)
            Case ActionTypes.Launch
                _working.Path = TextOf("path")
                _working.Args = TextOf("args")
            Case ActionTypes.Url : _working.Url = TextOf("url")
            Case ActionTypes.Command
                _working.Shell = Choice.GetValue(DirectCast(_inputs("shell"), ComboBox))
                _working.Command = TextOf("command")
                _working.Hidden = DirectCast(_inputs("hidden"), CheckBox).Checked
            Case ActionTypes.Obs
                _working.Op = Choice.GetValue(DirectCast(_inputs("op"), ComboBox))
                _working.Scene = TextOf("scene")
                _working.Source = TextOf("source")
            Case ActionTypes.Page, ActionTypes.Profile : _working.Target = Choice.GetValue(DirectCast(_inputs("target"), ComboBox))
            Case ActionTypes.Multi : _working.Actions = _steps.ToList()
            Case ActionTypes.Wait : _working.Ms = CInt(DirectCast(_inputs("ms"), NumericUpDown).Value)
        End Select
    End Sub

    Private Function TextOf(name As String) As String
        Dim s = _inputs(name).Text
        Return If(String.IsNullOrWhiteSpace(s), Nothing, s.Trim())
    End Function

    Private Sub TypeChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Collect()          ' Eingaben des bisherigen Typs merken (Wechsel hin und zurück verliert nichts)
        If SelectedType = ActionTypes.Page OrElse SelectedType = ActionTypes.Profile Then _working.Target = Nothing
        BuildFields()
    End Sub

    Private Sub TestClicked(sender As Object, e As EventArgs)
        Dim err = ValidateInput()
        If err IsNot Nothing Then
            MessageBox.Show(FindForm(), err, "Testen", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim a = Value
        If a IsNot Nothing Then _runner.Fire(a)
    End Sub

    ' ------------------------------------------------------------------ Felder

    Private Sub BuildFields()
        _fields.SuspendLayout()
        For Each c In _fields.Controls.Cast(Of Control).ToList()
            c.Dispose()
        Next
        _fields.Controls.Clear()
        _fields.RowStyles.Clear()
        _fields.RowCount = 0
        _inputs.Clear()
        _hints.Clear()
        _stepList = Nothing
        _shownType = SelectedType
        Dim a = _working

        Select Case _shownType
            Case ActionTypes.Hotkey
                Dim tb = AddTextRow("keys", "Tasten:", a.Keys)
                Dim capture As New CheckBox With {.Text = "Aufnehmen", .Appearance = Appearance.Button, .AutoSize = True}
                SetExtra(capture)
                HotkeyCapture.Attach(tb, capture)
                AddHint("z. B. Ctrl+Shift+M, Alt+F4, Win+D, F13 … F24. 'Aufnehmen' drücken, dann die Kombination auf der Tastatur drücken." &
                        " F13–F24 gibt es auf normalen Tastaturen nicht – einfach eintippen.")
            Case ActionTypes.Text
                Dim tb = AddTextRow("text", "Text:", a.Text, multiline:=True)
                tb.Height = 90
                Dim cb As New CheckBox With {.Text = "Über die Zwischenablage einfügen (Strg+V) – schneller bei langen Texten", .AutoSize = True, .Checked = a.Paste.GetValueOrDefault()}
                AddRow("", cb)
                _inputs("paste") = cb
            Case ActionTypes.Media
                AddComboRow("media", "Taste:", MediaChoices, If(a.MediaKey, "playpause"), editable:=False)
            Case ActionTypes.Sound
                Dim tb = AddTextRow("file", "Datei:", a.File)
                Dim builtIn As New Button With {.Text = "Mitgeliefert ▾", .AutoSize = True}
                AddHandler builtIn.Click, Sub() ShowBuiltInSounds(builtIn, tb)
                Dim soundButtons As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = Padding.Empty}
                soundButtons.Controls.Add(builtIn)
                soundButtons.Controls.Add(BrowseButton(tb, "Sounddateien (*.wav;*.mp3)|*.wav;*.mp3|Alle Dateien|*.*"))
                SetExtra(soundButtons)
                Dim vol As New NumericUpDown With {.Minimum = 0, .Maximum = 100, .Value = Math.Max(0, Math.Min(100, a.Volume.GetValueOrDefault(100))), .Width = 70}
                AddRow("Lautstärke %:", vol)
                vol.Anchor = AnchorStyles.Left Or AnchorStyles.Top
                _inputs("volume") = vol
                Dim dur As New NumericUpDown With {.Minimum = 0, .Maximum = 3600, .DecimalPlaces = 1, .Increment = 0.5D, .Width = 70,
                                                   .Value = CDec(Math.Max(0, Math.Min(3600, a.Duration.GetValueOrDefault())))}
                Dim durRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = Padding.Empty}
                durRow.Controls.Add(dur)
                durRow.Controls.Add(New Label With {.Text = "Sekunden (0 = ganze Datei)", .AutoSize = True, .ForeColor = Theme.Muted, .Margin = New Padding(3, 6, 3, 3)})
                AddRow("Dauer:", durRow)
                durRow.Anchor = AnchorStyles.Left Or AnchorStyles.Top
                _inputs("duration") = dur
                Dim devices = New List(Of Choice) From {New Choice("", "(Standard-Ausgabegerät)")}
                devices.AddRange(SoundActionHandler.GetOutputDevices().Select(Function(d) New Choice(d, d)))
                AddComboRow("device", "Ausgabe:", devices, If(a.Device, ""), editable:=True)
                AddHint("'Mitgeliefert' bietet 50 freie Sounds (CC0, BigSoundBank.com). Erneuter Tastendruck startet den Sound von vorn (Dauer 0) bzw. stoppt ihn (mit Dauer); mit Dauer wird am Ende kurz ausgeblendet.")
            Case ActionTypes.Launch
                Dim tb = AddTextRow("path", "Pfad:", a.Path)
                Dim pick As New Button With {.Text = "Datei …", .AutoSize = True}
                AddHandler pick.Click, Sub()
                                           Using d As New OpenFileDialog With {.Filter = "Programme und Dateien|*.*", .Title = "Programm oder Datei wählen"}
                                               If d.ShowDialog(FindForm()) = DialogResult.OK Then tb.Text = d.FileName
                                           End Using
                                       End Sub
                Dim folder As New Button With {.Text = "Ordner …", .AutoSize = True}
                AddHandler folder.Click, Sub()
                                             Using d As New FolderBrowserDialog()
                                                 If d.ShowDialog(FindForm()) = DialogResult.OK Then tb.Text = d.SelectedPath
                                             End Using
                                         End Sub
                Dim flow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False, .Margin = Padding.Empty}
                flow.Controls.Add(pick)
                flow.Controls.Add(folder)
                SetExtra(flow)
                AddTextRow("args", "Argumente:", a.Args)
                AddHint("Programm (z. B. calc.exe), beliebige Datei oder Ordner. Umgebungsvariablen wie %USERPROFILE% sind erlaubt.")
            Case ActionTypes.Url
                AddTextRow("url", "Adresse:", a.Url)
                AddHint("z. B. https://www.example.org – öffnet im Standardbrowser.")
            Case ActionTypes.Command
                AddComboRow("shell", "Shell:", {New Choice("cmd", "Eingabeaufforderung (cmd)"), New Choice("powershell", "Windows PowerShell"), New Choice("pwsh", "PowerShell 7 (pwsh)")},
                            If(a.Shell, "cmd"), editable:=False)
                Dim tb = AddTextRow("command", "Befehl:", a.Command, multiline:=True)
                tb.Height = 70
                SetExtra(BrowseButton(tb, "Skripte (*.ps1;*.bat;*.cmd)|*.ps1;*.bat;*.cmd|Alle Dateien|*.*"))
                Dim hidden As New CheckBox With {.Text = "Versteckt ausführen (Fehler erscheinen im Protokoll)", .AutoSize = True, .Checked = a.Hidden.GetValueOrDefault()}
                AddRow("", hidden)
                _inputs("hidden") = hidden
                AddHint("Sichtbar bleibt das Fenster offen, damit man die Ausgabe lesen kann. Bei PowerShell startet ein Pfad auf .ps1 das Skript.")
            Case ActionTypes.Obs
                AddComboRow("op", "Aktion:", ObsChoices, If(a.Op, "scene"), editable:=False)
                AddTextRow("scene", "Szene:", a.Scene)
                AddTextRow("source", "Quelle:", a.Source)
                AddHint("Szene/Quelle exakt wie in OBS benennen. Bei Quellen ohne Szene gilt die aktuelle Szene." & vbCrLf &
                        "OBS: Werkzeuge → WebSocket-Servereinstellungen → Server aktivieren; Adresse/Passwort unter Einstellungen.")
            Case ActionTypes.Page
                Dim items As New List(Of Choice) From {New Choice(ActionTypes.PageBack, "← Zurück (vorige Seite)"), New Choice(ActionTypes.PageFirst, "⌂ Erste Seite")}
                items.AddRange(_pageNames.Select(Function(n) New Choice(n, n)))
                AddComboRow("target", "Ziel:", items, If(a.Target, ActionTypes.PageBack), editable:=True)
            Case ActionTypes.Profile
                AddComboRow("target", "Profil:", _profileNames.Select(Function(n) New Choice(n, n)), If(a.Target, _profileNames.FirstOrDefault()), editable:=True)
                AddHint("Wechselt das manuell gewählte Profil.")
            Case ActionTypes.Multi
                BuildStepsEditor()
            Case ActionTypes.Wait
                Dim ms As New NumericUpDown With {.Minimum = 0, .Maximum = ActionRunner.MaxWaitMs, .Increment = 100, .Value = Math.Max(0, Math.Min(ActionRunner.MaxWaitMs, a.Ms.GetValueOrDefault(500))), .Width = 90}
                AddRow("Millisekunden:", ms)
                ms.Anchor = AnchorStyles.Left Or AnchorStyles.Top
                _inputs("ms") = ms
        End Select
        Theme.Apply(_fields)
        FitHints()
        _fields.ResumeLayout(True)
    End Sub

    Private Sub BuildStepsEditor()
        _stepList = New ListBox With {.Height = 190, .IntegralHeight = False, .DrawMode = DrawMode.OwnerDrawFixed, .ItemHeight = 30}
        AddHandler _stepList.DrawItem, AddressOf DrawStep
        RefreshSteps()
        AddHandler _stepList.DoubleClick, Sub() EditStep(_stepList.SelectedIndex)
        AddRow("Schritte:", _stepList)

        Dim buttons As New FlowLayoutPanel With {.FlowDirection = FlowDirection.TopDown, .AutoSize = True, .WrapContents = False, .Margin = Padding.Empty}
        buttons.Controls.Add(StepButton("Hinzufügen", Sub()
                                                          Using d As New ActionDialog(_runner, _pageNames, _profileNames, New ActionConfig With {.ActionType = ActionTypes.Hotkey})
                                                              If d.ShowDialog(FindForm()) = DialogResult.OK AndAlso d.Result IsNot Nothing Then
                                                                  _steps.Add(d.Result)
                                                                  RefreshSteps(_steps.Count - 1)
                                                              End If
                                                          End Using
                                                      End Sub))
        buttons.Controls.Add(StepButton("Bearbeiten", Sub() EditStep(_stepList.SelectedIndex)))
        buttons.Controls.Add(StepButton("Entfernen", Sub()
                                                         Dim i = _stepList.SelectedIndex
                                                         If i < 0 Then Return
                                                         _steps.RemoveAt(i)
                                                         RefreshSteps(Math.Min(i, _steps.Count - 1))
                                                     End Sub))
        buttons.Controls.Add(StepButton("Hoch", Sub() MoveStep(-1), Theme.GlyphUp))
        buttons.Controls.Add(StepButton("Runter", Sub() MoveStep(1), Theme.GlyphDown))
        SetExtra(buttons)
        AddHint("Die Schritte laufen nacheinander (Doppelklick = bearbeiten). Für Pausen den Schritt 'Warten' verwenden.")
    End Sub

    Private Shared Function StepButton(text As String, handler As Action, Optional glyph As String = Nothing) As ThemedButton
        Dim b As New ThemedButton(text, ButtonKind.Secondary, glyph) With {.BaseHeight = 30, .AutoSize = False, .Width = 104, .Height = 30, .Margin = New Padding(6, 0, 0, 6)}
        AddHandler b.Click, Sub() handler()
        Return b
    End Function

    ''' <summary>Schrittliste: Nummer, Typ-Etikett, Beschreibung.</summary>
    Private Sub DrawStep(sender As Object, e As DrawItemEventArgs)
        If e.Index < 0 OrElse e.Index >= _steps.Count Then Return
        Dim g = e.Graphics
        Dim selected = (e.State And DrawItemState.Selected) = DrawItemState.Selected
        Using b As New SolidBrush(If(selected, Theme.SelectedBg, Theme.InputBg))
            g.FillRectangle(b, e.Bounds)
        End Using
        Dim s = _steps(e.Index)
        Dim flags = TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix Or TextFormatFlags.SingleLine Or TextFormatFlags.EndEllipsis
        Dim x = e.Bounds.X + 6
        Dim num = (e.Index + 1).ToString("00")
        Dim mono = Theme.Mono(8.0F)
        Dim nw = TextRenderer.MeasureText(num, mono).Width
        TextRenderer.DrawText(g, num, mono, New Rectangle(x, e.Bounds.Y, nw, e.Bounds.Height), Theme.Muted, flags)
        x += nw + 6
        Dim chip = Theme.CaptionFor(s)
        Dim cf = Theme.UiSemi(7.5F)
        Dim cw = TextRenderer.MeasureText(chip, cf).Width + 10
        Dim chipRect As New Rectangle(x, e.Bounds.Y + 6, cw, e.Bounds.Height - 12)
        g.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
        Theme.FillRound(g, Theme.AccentDim, chipRect, 4)
        TextRenderer.DrawText(g, chip, cf, chipRect, Theme.AccentSoftText, TextFormatFlags.HorizontalCenter Or TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
        x += cw + 8
        TextRenderer.DrawText(g, ActionTypes.Describe(s), _stepList.Font, New Rectangle(x, e.Bounds.Y, e.Bounds.Right - x - 4, e.Bounds.Height), Theme.Fg, flags)
    End Sub

    Private Sub RefreshSteps(Optional selectIndex As Integer = -1)
        _stepList.Items.Clear()
        For i = 0 To _steps.Count - 1
            _stepList.Items.Add($"{i + 1}. {ActionTypes.Describe(_steps(i))}")
        Next
        If selectIndex >= 0 AndAlso selectIndex < _stepList.Items.Count Then _stepList.SelectedIndex = selectIndex
    End Sub

    Private Sub EditStep(index As Integer)
        If index < 0 OrElse index >= _steps.Count Then Return
        Using d As New ActionDialog(_runner, _pageNames, _profileNames, _steps(index))
            If d.ShowDialog(FindForm()) = DialogResult.OK Then
                If d.Result Is Nothing Then _steps.RemoveAt(index) Else _steps(index) = d.Result
                RefreshSteps(Math.Min(index, _steps.Count - 1))
            End If
        End Using
    End Sub

    Private Sub MoveStep(delta As Integer)
        Dim i = _stepList.SelectedIndex
        Dim j = i + delta
        If i < 0 OrElse j < 0 OrElse j >= _steps.Count Then Return
        Dim tmp = _steps(i)
        _steps(i) = _steps(j)
        _steps(j) = tmp
        RefreshSteps(j)
    End Sub

    ' --- Zeilen-Hilfen

    ''' <summary>Beschriftung über dem Feld (eigene Zeile), darunter das Feld; rechts optional ein Zusatz (SetExtra).</summary>
    Private Sub AddRow(label As String, control As Control)
        Dim text = label.TrimEnd(":"c).Trim()
        If text.Length > 0 Then
            Dim l = Theme.FieldLabel(text)
            l.Margin = Padding.Empty
            Dim lr = _fields.RowCount
            _fields.RowCount += 1
            _fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
            _fields.Controls.Add(l, 0, lr)
            _fields.SetColumnSpan(l, 2)
        End If
        Dim row = _fields.RowCount
        _fields.RowCount += 1
        _fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        control.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top
        control.Margin = New Padding(0, If(text.Length > 0, 0, 6), 0, 0)
        _fields.Controls.Add(control, 0, row)
    End Sub

    Private Sub SetExtra(control As Control)
        control.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        control.Margin = New Padding(6, 0, 0, 0)
        _fields.Controls.Add(control, 1, _fields.RowCount - 1)
    End Sub

    Private Sub AddHint(text As String)
        Dim row = _fields.RowCount
        _fields.RowCount += 1
        _fields.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Dim l As New Label With {.Text = text, .AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Margin = New Padding(0, 8, 0, 4)}
        _fields.Controls.Add(l, 0, row)
        _fields.SetColumnSpan(l, 2)
        _hints.Add(l)
    End Sub

    Private Function AddTextRow(name As String, label As String, value As String, Optional multiline As Boolean = False) As TextBox
        Dim tb As New TextBox With {.Text = If(value, ""), .Multiline = multiline, .AcceptsReturn = multiline, .ScrollBars = If(multiline, ScrollBars.Vertical, ScrollBars.None), .MinimumSize = New Size(140, 0)}
        AddRow(label, tb)
        _inputs(name) = tb
        Return tb
    End Function

    Private Function AddComboRow(name As String, label As String, items As IEnumerable(Of Choice), selected As String, editable As Boolean) As ComboBox
        Dim cb As New ComboBox With {.DropDownStyle = If(editable, ComboBoxStyle.DropDown, ComboBoxStyle.DropDownList), .MinimumSize = New Size(140, 0)}
        Choice.Fill(cb, items, selected)
        AddRow(label, cb)
        _inputs(name) = cb
        Return cb
    End Function

    ''' <summary>Menü mit den mitgelieferten Sounds; Auswahl trägt den relativen Pfad ein und spielt kurz vor.</summary>
    Private Sub ShowBuiltInSounds(anchor As Control, target As TextBox)
        Dim m As New ContextMenuStrip()
        For Each rel In SoundActionHandler.GetBuiltInSounds()
            Dim path = rel
            m.Items.Add(IO.Path.GetFileNameWithoutExtension(path), Nothing,
                        Sub()
                            target.Text = path
                            _runner.Fire(New ActionConfig With {.ActionType = ActionTypes.Sound, .File = path, .Volume = 80})
                        End Sub)
        Next
        If m.Items.Count = 0 Then m.Items.Add("(keine Sounds im Programmordner gefunden)").Enabled = False
        AddHandler m.Closed, Sub() BeginInvoke(Sub() m.Dispose())
        m.Show(anchor, New Point(0, anchor.Height))
    End Sub

    Private Function BrowseButton(target As TextBox, filter As String) As Control
        Dim b As New ThemedButton("Durchsuchen", ButtonKind.IconOnly, Theme.GlyphFolder) With {.BaseHeight = 30}
        AddHandler b.Click, Sub()
                                Using d As New OpenFileDialog With {.Filter = filter}
                                    If d.ShowDialog(FindForm()) = DialogResult.OK Then target.Text = d.FileName
                                End Using
                            End Sub
        Return b
    End Function
End Class

' ====================================================================== Hotkey aufnehmen

Friend Module HotkeyCapture
    <DllImport("user32.dll")>
    Private Function GetAsyncKeyState(vKey As Integer) As Short
    End Function

    Public Sub Attach(tb As TextBox, capture As CheckBox)
        AddHandler capture.CheckedChanged, Sub()
                                               If capture.Checked Then
                                                   tb.Focus()
                                                   tb.BackColor = Theme.AccentDim
                                               Else
                                                   tb.BackColor = Theme.InputBg
                                               End If
                                           End Sub
        AddHandler tb.PreviewKeyDown, Sub(s, e)
                                          If capture.Checked Then e.IsInputKey = True   ' auch Tab, Pfeile usw. aufnehmen
                                      End Sub
        AddHandler tb.KeyDown, Sub(s, e)
                                   If Not capture.Checked Then Return
                                   e.SuppressKeyPress = True
                                   e.Handled = True
                                   Dim parts As New List(Of String)
                                   If e.Control Then parts.Add("Ctrl")
                                   If e.Shift Then parts.Add("Shift")
                                   If e.Alt Then parts.Add("Alt")
                                   If GetAsyncKeyState(&H5B) < 0 OrElse GetAsyncKeyState(&H5C) < 0 Then parts.Add("Win")
                                   Dim main = KeyName(e.KeyCode)
                                   If main IsNot Nothing Then parts.Add(main)
                                   tb.Text = String.Join("+", parts)
                                   If main IsNot Nothing Then capture.Checked = False   ' fertig
                               End Sub
        AddHandler tb.Leave, Sub() capture.Checked = False
    End Sub

    ''' <summary>Name der Taste im Format von Hotkeys.ParseHotkey; Nothing für reine Modifier.</summary>
    Public Function KeyName(k As Keys) As String
        Select Case k
            Case Keys.A To Keys.Z : Return ChrW(k).ToString()
            Case Keys.D0 To Keys.D9 : Return CInt(k - Keys.D0).ToString()
            Case Keys.F1 To Keys.F24 : Return "F" & CInt(k - Keys.F1 + 1)
            Case Keys.NumPad0 To Keys.NumPad9 : Return "Num" & CInt(k - Keys.NumPad0)
            Case Keys.Enter : Return "Enter"
            Case Keys.Escape : Return "Esc"
            Case Keys.Space : Return "Space"
            Case Keys.Back : Return "Backspace"
            Case Keys.Delete : Return "Delete"
            Case Keys.Insert : Return "Insert"
            Case Keys.Home : Return "Home"
            Case Keys.End : Return "End"
            Case Keys.PageUp : Return "PageUp"
            Case Keys.PageDown : Return "PageDown"
            Case Keys.Left : Return "Left"
            Case Keys.Up : Return "Up"
            Case Keys.Right : Return "Right"
            Case Keys.Down : Return "Down"
            Case Keys.Tab : Return "Tab"
            Case Keys.PrintScreen : Return "PrintScreen"
            Case Keys.Pause : Return "Pause"
            Case Keys.Apps : Return "Apps"
            Case Keys.Multiply : Return "Multiply"
            Case Keys.Add : Return "Add"
            Case Keys.Subtract : Return "Subtract"
            Case Keys.Divide : Return "Divide"
            Case Keys.Decimal : Return "Decimal"
            Case Keys.Oemplus : Return "Plus"
            Case Keys.OemMinus : Return "Minus"
            Case Keys.Oemcomma : Return "Comma"
            Case Keys.OemPeriod : Return "Period"
            Case Keys.CapsLock : Return "CapsLock"
            Case Keys.NumLock : Return "NumLock"
            Case Keys.Scroll : Return "ScrollLock"
            Case Else : Return Nothing
        End Select
    End Function
End Module

' ====================================================================== Dialog für einen Schritt

Public Class ActionDialog
    Inherits ThemedForm

    Private ReadOnly _editor As ActionEditor
    Public Property Result As ActionConfig

    Public Sub New(runner As ActionRunner, pageNames As IList(Of String), profileNames As IList(Of String), action As ActionConfig)
        Text = "Schritt bearbeiten"
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        MinimizeBox = False
        ClientSize = New Size(620, 520)
        MinimumSize = New Size(480, 360)

        _editor = New ActionEditor(runner, pageNames, profileNames) With {.Dock = DockStyle.Top}
        _editor.LoadAction(action)
        Dim scroll As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(24, 18, 24, 12)}
        scroll.Controls.Add(_editor)

        Dim ok As New ThemedButton("Übernehmen", ButtonKind.Primary)
        Dim cancel As New ThemedButton("Abbrechen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}
        AddHandler ok.Click, Sub()
                                 Dim err = _editor.ValidateInput()
                                 If err IsNot Nothing Then
                                     MessageBox.Show(Me, err, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning)
                                     Return
                                 End If
                                 Result = _editor.Value
                                 DialogResult = DialogResult.OK
                             End Sub
        Controls.Add(scroll)
        Controls.Add(MakeFooter(Nothing, cancel, ok))
        CancelButton = cancel
    End Sub
End Class

' ====================================================================== Texteingabe

''' <summary>Kleiner Eingabedialog im App-Design (ersetzt InputBox).</summary>
Public Class TextPrompt
    Inherits ThemedForm

    Private ReadOnly _box As New TextBox
    Private ReadOnly _allowEmpty As Boolean

    Private Sub New(title As String, prompt As String, value As String, allowEmpty As Boolean)
        _allowEmpty = allowEmpty
        Text = title
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        ClientSize = New Size(460, 200)

        Dim body As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(24, 20, 24, 12)}
        Dim l As New Label With {.Text = prompt, .AutoSize = True, .MaximumSize = New Size(410, 0), .ForeColor = Theme.FgSoft, .Padding = New Padding(0, 0, 0, 10)}
        _box.Text = value
        _box.Font = Theme.Ui(10.5F)
        Theme.StackTop(body, l, _box)

        Dim ok As New ThemedButton("OK", ButtonKind.Primary)
        Dim cancel As New ThemedButton("Abbrechen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}
        AddHandler ok.Click, Sub()
                                 If Not _allowEmpty AndAlso String.IsNullOrWhiteSpace(_box.Text) Then
                                     _box.Focus()
                                     Return
                                 End If
                                 DialogResult = DialogResult.OK
                             End Sub
        Controls.Add(body)
        Controls.Add(MakeFooter(Nothing, cancel, ok))
        AcceptButton = ok
        CancelButton = cancel
        Theme.Apply(Me)
        AddHandler Shown, Sub()
                              ClientSize = New Size(ClientSize.Width, l.Height + _box.Height + Theme.PxI(Me, 130))
                              _box.SelectAll()
                              _box.Focus()
                          End Sub
    End Sub

    ''' <summary>Fragt einen Text ab. Nothing = abgebrochen.</summary>
    Public Shared Function Ask(owner As IWin32Window, title As String, prompt As String, value As String, Optional allowEmpty As Boolean = False) As String
        Using d As New TextPrompt(title, prompt, value, allowEmpty)
            If d.ShowDialog(owner) <> DialogResult.OK Then Return Nothing
            Return d._box.Text
        End Using
    End Function
End Class

' ====================================================================== Einstellungen

Public Class SettingsForm
    Inherits ThemedForm

    Private ReadOnly _longPress As New NumericUpDown With {.Minimum = 200, .Maximum = 3000, .Increment = 50, .Width = 90, .TextAlign = HorizontalAlignment.Right}
    Private ReadOnly _obsUrl As New TextBox
    Private ReadOnly _obsPassword As New TextBox With {.UseSystemPasswordChar = True}
    Private ReadOnly _autostart As New CheckBox With {.Text = "Mit Windows starten", .AutoSize = True}

    Public ReadOnly Property LongPressMs As Integer
        Get
            Return CInt(_longPress.Value)
        End Get
    End Property

    Public ReadOnly Property ObsUrl As String
        Get
            Return If(String.IsNullOrWhiteSpace(_obsUrl.Text), "ws://localhost:4455", _obsUrl.Text.Trim())
        End Get
    End Property

    Public ReadOnly Property ObsPassword As String
        Get
            Return _obsPassword.Text
        End Get
    End Property

    Public ReadOnly Property AutostartEnabled As Boolean
        Get
            Return _autostart.Checked
        End Get
    End Property

    Public Sub New(config As DeckConfig, autostartOn As Boolean, Optional portName As String = Nothing, Optional configPath As String = Nothing)
        Text = "Einstellungen"
        StartPosition = FormStartPosition.CenterParent
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        ShowInTaskbar = False
        ClientSize = New Size(600, 640)

        _longPress.Value = Math.Max(_longPress.Minimum, Math.Min(_longPress.Maximum, config.LongPressMs))
        _obsUrl.Text = config.Obs?.Url
        _obsPassword.Text = config.Obs?.Password
        _autostart.Checked = autostartOn
        _obsUrl.Font = Theme.Mono(9.5F)

        ' --- Gerät
        Dim device As New CardPanel With {.Fill = Theme.Raised, .Height = 68}
        Dim connected = portName IsNot Nothing
        AddHandler device.Paint, Sub(s, e)
                                     e.Graphics.SmoothingMode = Drawing2D.SmoothingMode.AntiAlias
                                     Theme.Dot(e.Graphics, If(connected, Theme.Good, Theme.Bad), Theme.Px(device, 22), Theme.Px(device, 24), Theme.Px(device, 8))
                                     TextRenderer.DrawText(e.Graphics, If(connected, $"Verbunden mit {portName}", "Kein Gerät verbunden"), Theme.UiSemi(10.0F),
                                                           New Point(Theme.PxI(device, 34), Theme.PxI(device, 13)), Theme.Fg)
                                     TextRenderer.DrawText(e.Graphics, If(connected, $"HELLO STREAMDECK {DeviceProtocol.SupportedVersion} · PING alle 1 s", "Die App sucht automatisch auf allen COM-Ports."),
                                                           If(connected, Theme.Mono(8.0F), Theme.Ui(8.5F)), New Point(Theme.PxI(device, 34), Theme.PxI(device, 37)), Theme.Muted)
                                 End Sub

        ' --- Allgemein
        Dim autostartHint As New Label With {.Text = "Startet unsichtbar im Infobereich der Taskleiste.", .AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Padding = New Padding(20, 0, 0, 8)}
        Dim longRow As New FlowLayoutPanel With {.AutoSize = True, .WrapContents = False}
        longRow.Controls.Add(_longPress)
        longRow.Controls.Add(New Label With {.Text = "ms – so lange muss eine Taste gehalten werden", .AutoSize = True, .ForeColor = Theme.Muted, .Margin = New Padding(6, 5, 0, 0)})

        ' --- OBS
        Dim obsHint As New Label With {.Text = "In OBS: Werkzeuge › WebSocket-Servereinstellungen › 'WebSocket-Server aktivieren'. Adresse und Passwort stehen unter 'Verbindungsinformationen anzeigen'. Das Passwort wird unverschlüsselt in config.json gespeichert.",
                                       .AutoSize = True, .MaximumSize = New Size(550, 0), .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Padding = New Padding(0, 8, 0, 0)}

        Dim body As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(24, 6, 24, 12)}
        Theme.StackTop(body,
                       Section("Gerät"), device,
                       Section("Allgemein"), _autostart, autostartHint, Theme.FieldLabel("Langer Druck ab"), longRow,
                       Section("OBS WebSocket"), Theme.FieldLabel("Adresse"), _obsUrl, Theme.FieldLabel("Passwort"), _obsPassword, obsHint)

        Dim folderLink As New LinkLabel With {.Text = "Konfigurationsordner öffnen", .AutoSize = True, .Padding = New Padding(0, 9, 0, 0), .Visible = configPath IsNot Nothing}
        AddHandler folderLink.LinkClicked, Sub()
                                               Process.Start(New ProcessStartInfo(IO.Path.GetDirectoryName(configPath)) With {.UseShellExecute = True})
                                           End Sub
        Dim ok As New ThemedButton("Speichern", ButtonKind.Primary) With {.DialogResult = DialogResult.OK}
        Dim cancel As New ThemedButton("Abbrechen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}

        Controls.Add(body)
        Controls.Add(MakeFooter(folderLink, cancel, ok))
        AcceptButton = ok
        CancelButton = cancel
        Theme.Apply(Me)
    End Sub

    Private Shared Function Section(title As String) As Label
        Dim l = Theme.SectionLabel(title)
        l.Padding = New Padding(0, 18, 0, 8)
        Return l
    End Function
End Class
