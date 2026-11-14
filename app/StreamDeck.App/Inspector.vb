' Inspektor rechts im Hauptfenster: bearbeitet die ausgewählte Taste direkt (ersetzt den früheren
' modalen Tasteneditor). Beschriftung, Symbol, Reiter „Kurzer Druck / Langer Druck“, Testen, Speichern.
Imports System.Text.Json
Imports StreamDeck.Core

Public Class KeyInspector
    Inherits BorderPanel

    Private ReadOnly _runner As ActionRunner
    Private ReadOnly _posLabel As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F)}
    Private ReadOnly _titleLabel As New Label With {.AutoSize = False, .AutoEllipsis = True, .ForeColor = Theme.Fg, .Font = Theme.UiBold(14.0F), .Height = 34}
    Private ReadOnly _badge As New Label With {.AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Mono(8.0F), .Padding = New Padding(6, 3, 6, 3)}
    Private ReadOnly _labelBox As New TextBox
    Private ReadOnly _iconBox As New TextBox
    Private ReadOnly _tabs As New Segmented("Kurzer Druck", "Langer Druck")
    Private ReadOnly _pressHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _longHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink, .Visible = False}
    Private ReadOnly _longEditorHost As New Panel With {.AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink}
    Private ReadOnly _useLong As New CheckBox With {.Text = "Andere Aktion bei langem Druck", .AutoSize = True, .Padding = New Padding(0, 6, 0, 6)}
    Private ReadOnly _clearButton As New ThemedButton("Taste leeren", ButtonKind.Danger)
    Private ReadOnly _testButton As New ThemedButton("Testen", ButtonKind.Secondary, Theme.GlyphPlay)
    Private ReadOnly _saveButton As New ThemedButton("Speichern", ButtonKind.Primary)
    Private _press As ActionEditor
    Private _long As ActionEditor
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

        ' --- Langer Druck
        Dim longHint As New Label With {.Text = "Ist ein langer Druck belegt, löst der kurze Druck erst beim Loslassen aus. Die Haltedauer steht in den Einstellungen.",
                                        .AutoSize = True, .ForeColor = Theme.Muted, .Font = Theme.Ui(8.5F), .Padding = New Padding(0, 0, 0, 8)}
        AddHandler _longHost.SizeChanged, Sub() longHint.MaximumSize = New Size(Math.Max(120, _longHost.Width), 0)
        Theme.StackTop(_longHost, _useLong, longHint, _longEditorHost)
        AddHandler _useLong.CheckedChanged, Sub()
                                                If _long IsNot Nothing Then _long.Enabled = _useLong.Checked
                                            End Sub

        ' --- Scrollbarer Inhalt
        Dim scroll As New Panel With {.Dock = DockStyle.Fill, .AutoScroll = True, .Padding = New Padding(0, 0, 4, 12)}
        Dim gap As New Panel With {.Height = 16}
        Dim gap2 As New Panel With {.Height = 8}
        Theme.StackTop(scroll, Theme.FieldLabel("Beschriftung"), _labelBox, Theme.FieldLabel("Symbol (Bild oder Programm, optional)"), iconRow, gap, _tabs, gap2, _pressHost, _longHost)

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

        AddHandler _tabs.SelectedIndexChanged, Sub()
                                                   _pressHost.Visible = _tabs.SelectedIndex = 0
                                                   _longHost.Visible = _tabs.SelectedIndex = 1
                                               End Sub
        AddHandler _labelBox.TextChanged, Sub() UpdateTitle()
        AddHandler _saveButton.Click, Sub() TrySave()
        AddHandler _testButton.Click, Sub() TestCurrent()
        AddHandler _clearButton.Click, Sub() ClearKey()
        Theme.Apply(Me)
        Enabled = False
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
        _iconBox.Text = If(key?.Icon, "")

        ' Editoren neu anlegen (Seiten-/Profilnamen können sich geändert haben)
        _press?.Dispose()
        _long?.Dispose()
        _press = New ActionEditor(_runner, pageNames, profileNames) With {.Dock = DockStyle.Top, .ShowTestButton = False}
        _long = New ActionEditor(_runner, pageNames, profileNames) With {.Dock = DockStyle.Top, .ShowTestButton = False}
        _pressHost.Controls.Add(_press)
        _longEditorHost.Controls.Add(_long)
        _press.LoadAction(key?.Press)
        _long.LoadAction(key?.LongPress)
        _useLong.Checked = key?.LongPress IsNot Nothing
        _long.Enabled = _useLong.Checked

        _tabs.SelectedIndex = 0
        _clearButton.Enabled = key IsNot Nothing
        Enabled = True
        UpdateTitle()
        ResumeLayout(True)
        _baselineJson = Serialize(BuildKey())
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
            .Icon = If(String.IsNullOrWhiteSpace(_iconBox.Text), Nothing, _iconBox.Text.Trim()),
            .Press = _press.Value,
            .LongPress = If(_useLong.Checked, _long.Value, Nothing)}
        If k.Label Is Nothing AndAlso k.Icon Is Nothing AndAlso k.Press Is Nothing AndAlso k.LongPress Is Nothing Then Return Nothing
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
        If _useLong.Checked Then
            err = _long.ValidateInput()
            If err IsNot Nothing Then
                _tabs.SelectedIndex = 1
                MessageBox.Show(FindForm(), "Langer Druck: " & err, "Taste speichern", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If
        End If
        Dim k = BuildKey()
        _StoredJson = Serialize(k)
        _baselineJson = _StoredJson
        _clearButton.Enabled = k IsNot Nothing
        UpdateTitle()
        RaiseEvent SaveRequested(_KeyIndex, k, _ProfileName, _PageName)
        Return True
    End Function

    Private Sub TestCurrent()
        Dim editor = If(_tabs.SelectedIndex = 0, _press, _long)
        If editor Is Nothing Then Return
        If _tabs.SelectedIndex = 1 AndAlso Not _useLong.Checked Then Return
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
