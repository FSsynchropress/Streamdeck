' Inspektor rechts im Hauptfenster: bearbeitet die ausgewählte Taste direkt.
' Beschriftung, Beschreibung, Symbol, Reiter Kurz / Lang / Doppelt / Loslassen, Testen, Speichern.
Imports System.Text.Json
Imports StreamDeck.Core

Public Class KeyInspector
    Inherits BorderPanel

    ''' <summary>Ein optionaler Reiter (lang, doppelt, loslassen): Häkchen + Hinweis + Aktionseditor.</summary>
    Private Class OptionalSlot
        Public ReadOnly Host As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Visible = False}
        Public ReadOnly EditorHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
        Public ReadOnly Use As CheckBox
        Public ReadOnly Name As String
        Public Editor As ActionEditor

        Public Sub New(name As String, checkText As String, hint As String)
            Me.Name = name
            Use = New CheckBox With {.Text = checkText, .AutoSize = True, .Padding = New Padding(0, 6, 0, 6)}
            Dim hintLabel As New Label With {.Text = hint, .AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Padding = New Padding(0, 0, 0, 8)}
            AddHandler Host.SizeChanged, Sub() hintLabel.MaximumSize = New Size(Math.Max(120, Host.Width), 0)
            Theme.StackTop(Host, Use, hintLabel, EditorHost)
            AddHandler Use.CheckedChanged, Sub()
                                               If Editor IsNot Nothing Then Editor.Enabled = Use.Checked
                                           End Sub
        End Sub

        Public ReadOnly Property Value As ActionConfig
            Get
                Return If(Use.Checked AndAlso Editor IsNot Nothing, Editor.Value, Nothing)
            End Get
        End Property
    End Class

    Private ReadOnly _runner As ActionRunner
    Private ReadOnly _posLabel As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F)}
    Private ReadOnly _titleLabel As New Label With {.AutoSize = False, .AutoEllipsis = True, .ForeColor = Theme.Fg, .Font = Theme.UiBold(14.0F), .Height = 34}
    Private ReadOnly _badge As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Mono(8.0F), .Padding = New Padding(6, 3, 6, 3)}
    Private ReadOnly _labelBox As New TextBox
    Private ReadOnly _descBox As New TextBox With {.Multiline = True, .Height = 44, .ScrollBars = ScrollBars.Vertical}
    Private ReadOnly _iconBox As New TextBox
    Private ReadOnly _tabs As New Segmented("Kurz", "Lang", "Doppelt", "Loslassen")
    Private ReadOnly _pressHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _pressEditorHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _repeat As New CheckBox With {.Text = "Wiederholen, solange die Taste gehalten wird", .AutoSize = True, .Padding = New Padding(0, 8, 0, 4)}
    Private ReadOnly _long As New OptionalSlot("Langer Druck", "Andere Aktion bei langem Druck",
        "Ist ein langer Druck belegt, löst der kurze Druck erst beim Loslassen aus. Die Haltedauer steht in den Einstellungen.")
    Private ReadOnly _double As New OptionalSlot("Doppeldruck", "Aktion bei Doppeldruck",
        "Ist ein Doppeldruck belegt, wartet der kurze Druck nach dem Loslassen kurz, ob ein zweiter Druck folgt (Zeitfenster in den Einstellungen).")
    Private ReadOnly _release As New OptionalSlot("Loslassen", "Aktion beim Loslassen",
        "Läuft bei jedem Loslassen, zusätzlich zum kurzen Druck. Beispiel Push-to-Talk: kurz = 'Mikrofon einschalten', Loslassen = 'Mikrofon stumm schalten'.")
    Private ReadOnly _clearButton As New ThemedButton("Taste leeren", ButtonKind.Danger)
    Private ReadOnly _testButton As New ThemedButton("Testen", ButtonKind.Secondary, Theme.GlyphPlay)
    Private ReadOnly _saveButton As New ThemedButton("Speichern", ButtonKind.Primary)
    Private _press As ActionEditor
    Private _pageNames As IList(Of String) = New List(Of String)
    Private _profileNames As IList(Of String) = New List(Of String)
    Private _baselineJson As String = "null"

    Public ReadOnly Property KeyIndex As Integer = -1
    Public ReadOnly Property ProfileName As String
    Public ReadOnly Property PageName As String
    ''' <summary>Gespeicherte Belegung beim Laden (JSON), um Änderungen von außen zu erkennen.</summary>
    Public ReadOnly Property StoredJson As String = "null"

    ''' <summary>Speichern (oder Leeren, dann key = Nothing).</summary>
    Public Event SaveRequested(index As Integer, key As KeyConfig, profileName As String, pageName As String)

    Private ReadOnly Property Slots As OptionalSlot()
        Get
            Return {_long, _double, _release}
        End Get
    End Property

    Public Sub New(runner As ActionRunner)
        _runner = runner
        Borders = AnchorStyles.Left
        BackColor = Theme.Surface
        ForeColor = Theme.Fg
        Padding = New Padding(22, 18, 22, 0)

        ' --- Kopf
        Dim head As New TableLayoutPanel With {.ColumnCount = 2, .RowCount = 2, .AutoSize = True, .Padding = New Padding(0, 0, 0, 8)}
        head.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        head.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        _posLabel.Margin = New Padding(0, 0, 0, 2)
        _titleLabel.Margin = Padding.Empty
        _titleLabel.Dock = DockStyle.Fill
        _badge.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        AddHandler _badge.Paint, Sub(s, e)
                                     Using p As New Pen(Theme.BorderColor)
                                         e.Graphics.DrawRectangle(p, 0, 0, _badge.Width - 1, _badge.Height - 1)
                                     End Using
                                 End Sub
        head.Controls.Add(_posLabel, 0, 0)
        head.Controls.Add(_badge, 1, 0)
        head.Controls.Add(_titleLabel, 0, 1)
        head.SetColumnSpan(_titleLabel, 2)

        ' --- Symbolzeile
        Dim iconRow As New TableLayoutPanel With {.ColumnCount = 3, .AutoSize = True}
        iconRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
        iconRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        iconRow.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        _iconBox.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        _iconBox.Margin = New Padding(0, 3, 4, 0)
        Dim browse As New ThemedButton("Symbol wählen", ButtonKind.IconOnly, Theme.GlyphFolder) With {.BaseHeight = 30, .Margin = Padding.Empty}
        Dim clearIcon As New ThemedButton("Symbol entfernen", ButtonKind.IconOnly, Theme.GlyphClose) With {.BaseHeight = 30, .Margin = Padding.Empty}
        AddHandler browse.Click, Sub()
                                     Using d As New OpenFileDialog With {.Filter = "Bilder und Programme|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.exe|Alle Dateien|*.*", .Title = "Symbol wählen"}
                                         If d.ShowDialog(FindForm()) = DialogResult.OK Then _iconBox.Text = d.FileName
                                     End Using
                                 End Sub
        AddHandler clearIcon.Click, Sub() _iconBox.Text = ""
        iconRow.Controls.Add(_iconBox, 0, 0)
        iconRow.Controls.Add(browse, 1, 0)
        iconRow.Controls.Add(clearIcon, 2, 0)

        ' --- Kurzer Druck: Editor + Wiederholen
        Dim repeatHint As New Label With {.Text = "z. B. für Lautstärke. Wirkt nur, wenn weder langer Druck noch Doppeldruck belegt sind.",
                                          .AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Padding = New Padding(20, 0, 0, 4)}
        AddHandler _pressHost.SizeChanged, Sub() repeatHint.MaximumSize = New Size(Math.Max(120, _pressHost.Width), 0)
        Theme.StackTop(_pressHost, _pressEditorHost, _repeat, repeatHint)

        ' --- Scrollbarer Inhalt
        Dim scroll As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(0, 0, 4, 12)}
        Dim gap As New Panel With {.Height = 16}
        Dim gap2 As New Panel With {.Height = 8}
        Theme.StackTop(scroll, Theme.FieldLabel("Beschriftung"), _labelBox, Theme.FieldLabel("Beschreibung (optional)"), _descBox,
                       Theme.FieldLabel("Symbol (Bild oder Programm, optional)"), iconRow, gap, _tabs, gap2,
                       _pressHost, _long.Host, _double.Host, _release.Host)

        ' --- Fuß
        Dim footer As New BorderPanel With {.Dock = DockStyle.Bottom, .Borders = AnchorStyles.Top, .Height = 68, .Padding = New Padding(0, 16, 0, 16)}
        Dim right As New FlowLayoutPanel With {.Dock = DockStyle.Right, .AutoSize = True, .FlowDirection = FlowDirection.LeftToRight, .WrapContents = False}
        right.Controls.Add(_testButton)
        right.Controls.Add(_saveButton)
        _clearButton.Dock = DockStyle.Left
        footer.Controls.Add(right)
        footer.Controls.Add(_clearButton)

        Controls.Add(scroll)
        Controls.Add(footer)
        head.Dock = DockStyle.Top
        Controls.Add(head)

        AddHandler _tabs.SelectedIndexChanged, Sub() ShowTab(_tabs.SelectedIndex)
        AddHandler _labelBox.TextChanged, Sub() UpdateTitle()
        AddHandler _saveButton.Click, Sub() TrySave()
        AddHandler _testButton.Click, Sub() TestCurrent()
        AddHandler _clearButton.Click, Sub() ClearKey()
        Theme.Apply(Me)
        Enabled = False
    End Sub

    Private Sub ShowTab(index As Integer)
        _pressHost.Visible = index = 0
        For i = 0 To Slots.Length - 1
            Slots(i).Host.Visible = index = i + 1
        Next
    End Sub

    Public Shared Function Serialize(key As KeyConfig) As String
        Return JsonSerializer.Serialize(key, ConfigStore.JsonOptions)
    End Function

    ' ------------------------------------------------------------------ Laden

    Public Sub LoadKey(index As Integer, key As KeyConfig, profileName As String, pageName As String, pageNames As IList(Of String), profileNames As IList(Of String))
        SuspendLayout()
        _KeyIndex = index
        _ProfileName = profileName
        _PageName = pageName
        _pageNames = pageNames
        _profileNames = profileNames
        _StoredJson = Serialize(key)

        _posLabel.Text = $"Taste {index} · Reihe {index \ 5 + 1}, Spalte {index Mod 5 + 1} · Seite {pageName}"
        _badge.Text = "#" & index
        _labelBox.Text = If(key?.Label, "")
        _descBox.Text = If(key?.Description, "")
        _iconBox.Text = If(key?.Icon, "")
        _repeat.Checked = key IsNot Nothing AndAlso key.Repeat.GetValueOrDefault()

        ' Editoren neu anlegen (Seiten-/Profilnamen können sich geändert haben)
        _press?.Dispose()
        _press = NewEditor()
        _pressEditorHost.Controls.Add(_press)
        _press.LoadAction(key?.Press)
        LoadSlot(_long, key?.LongPress)
        LoadSlot(_double, key?.DoublePress)
        LoadSlot(_release, key?.Release)

        _tabs.SelectedIndex = 0
        ShowTab(0)
        _clearButton.Enabled = key IsNot Nothing
        Enabled = True
        UpdateTitle()
        ResumeLayout(True)
        _baselineJson = Serialize(BuildKey())
    End Sub

    Private Function NewEditor() As ActionEditor
        Return New ActionEditor(_runner, _pageNames, _profileNames) With {.Dock = DockStyle.Top, .ShowTestButton = False}
    End Function

    Private Sub LoadSlot(slot As OptionalSlot, action As ActionConfig)
        slot.Editor?.Dispose()
        slot.Editor = NewEditor()
        slot.EditorHost.Controls.Add(slot.Editor)
        slot.Editor.LoadAction(action)
        slot.Use.Checked = action IsNot Nothing
        slot.Editor.Enabled = slot.Use.Checked
    End Sub

    ''' <summary>Drag-and-drop aus der Aktionsleiste: Aktionstyp für den kurzen Druck vorwählen (noch nicht gespeichert).</summary>
    Public Sub ApplyActionType(actionType As String)
        If _press Is Nothing Then Return
        _tabs.SelectedIndex = 0
        ShowTab(0)
        _press.LoadAction(New ActionConfig With {.ActionType = actionType})
        If _labelBox.Text.Trim().Length = 0 Then _labelBox.Text = Theme.CaptionFor(New ActionConfig With {.ActionType = actionType})
        _press.Focus()
    End Sub

    Private Sub UpdateTitle()
        Dim t = _labelBox.Text.Trim()
        _titleLabel.Text = If(t.Length > 0, t, If(_StoredJson = "null", "Nicht belegt", "(ohne Beschriftung)"))
    End Sub

    ' ------------------------------------------------------------------ Zustand

    ''' <summary>Belegung aus den Eingaben (ohne Prüfung). Nothing = leer.</summary>
    Public Function BuildKey() As KeyConfig
        If _press Is Nothing Then Return Nothing
        Dim k As New KeyConfig With {
            .Label = If(String.IsNullOrWhiteSpace(_labelBox.Text), Nothing, _labelBox.Text.Trim()),
            .Description = If(String.IsNullOrWhiteSpace(_descBox.Text), Nothing, _descBox.Text.Trim()),
            .Icon = If(String.IsNullOrWhiteSpace(_iconBox.Text), Nothing, _iconBox.Text.Trim()),
            .Press = _press.Value,
            .LongPress = _long.Value,
            .DoublePress = _double.Value,
            .Release = _release.Value,
            .Repeat = If(_repeat.Checked, True, CType(Nothing, Boolean?))}
        If k.Label Is Nothing AndAlso k.Icon Is Nothing AndAlso k.Description Is Nothing AndAlso k.Press Is Nothing AndAlso
           k.LongPress Is Nothing AndAlso k.DoublePress Is Nothing AndAlso k.Release Is Nothing Then Return Nothing
        Return k
    End Function

    ''' <summary>Ungespeicherte Änderungen vorhanden?</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return _KeyIndex >= 0 AndAlso _press IsNot Nothing AndAlso Serialize(BuildKey()) <> _baselineJson
        End Get
    End Property

    ''' <summary>Prüft und speichert. False, wenn die Eingaben fehlerhaft sind.</summary>
    Public Function TrySave() As Boolean
        If _KeyIndex < 0 OrElse _press Is Nothing Then Return False
        Dim err = _press.ValidateInput()
        If err IsNot Nothing Then
            _tabs.SelectedIndex = 0
            MessageBox.Show(FindForm(), "Kurzer Druck: " & err, "Taste speichern", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return False
        End If
        For i = 0 To Slots.Length - 1
            Dim slot = Slots(i)
            If Not slot.Use.Checked Then Continue For
            err = slot.Editor.ValidateInput()
            If err IsNot Nothing Then
                _tabs.SelectedIndex = i + 1
                MessageBox.Show(FindForm(), $"{slot.Name}: {err}", "Taste speichern", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
        Next
        Dim k = BuildKey()
        _StoredJson = Serialize(k)
        _baselineJson = _StoredJson
        _clearButton.Enabled = k IsNot Nothing
        UpdateTitle()
        RaiseEvent SaveRequested(_KeyIndex, k, _ProfileName, _PageName)
        Return True
    End Function

    Private Sub TestCurrent()
        Dim editor As ActionEditor
        If _tabs.SelectedIndex = 0 Then
            editor = _press
        Else
            Dim slot = Slots(_tabs.SelectedIndex - 1)
            If Not slot.Use.Checked Then Return
            editor = slot.Editor
        End If
        If editor Is Nothing Then Return
        Dim err = editor.ValidateInput()
        If err IsNot Nothing Then
            MessageBox.Show(FindForm(), err, "Testen", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim a = editor.Value
        If a IsNot Nothing Then _runner.Fire(a, _KeyIndex)
    End Sub

    Private Sub ClearKey()
        If MessageBox.Show(FindForm(), $"Belegung von Taste {_KeyIndex} entfernen?", "Taste leeren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        Dim idx = _KeyIndex
        Dim prof = _ProfileName
        Dim pg = _PageName
        LoadKey(idx, Nothing, prof, pg, _pageNames, _profileNames)
        RaiseEvent SaveRequested(idx, Nothing, prof, pg)
    End Sub
End Class
