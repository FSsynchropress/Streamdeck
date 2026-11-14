' Aktionsleiste (Chips zum Ziehen auf eine Taste) und Druckvorlage für die Keycap-Einleger.
Imports System.Drawing.Drawing2D
Imports System.Drawing.Printing
Imports StreamDeck.Core

' ====================================================================== Aktions-Chip

''' <summary>Ein Aktionstyp zum Ziehen auf eine Taste (Drag-and-drop).</summary>
Public Class ActionChip
    Inherits Control

    Public Const DataFormat As String = "StreamDeckActionType"

    Private ReadOnly _actionType As String
    Private _hover As Boolean
    Private _downAt As Point?

    Public Sub New(actionType As String)
        _actionType = actionType
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.OptimizedDoubleBuffer Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
        Cursor = Cursors.SizeAll
        Font = Theme.UiSemi(8.5F)
        Dim caption = Theme.CaptionFor(New ActionConfig With {.ActionType = actionType})
        Text = caption
        AccessibleName = $"Aktion {caption} – auf eine Taste ziehen"
        Dim w = TextRenderer.MeasureText(caption, Font).Width
        Size = New Size(w + 38, 28)
        Margin = New Padding(0, 0, 6, 6)
    End Sub

    Protected Overrides Sub OnMouseEnter(e As EventArgs)
        _hover = True
        Invalidate()
        MyBase.OnMouseEnter(e)
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        _hover = False
        Invalidate()
        MyBase.OnMouseLeave(e)
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        If e.Button = MouseButtons.Left Then _downAt = e.Location
        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        If _downAt.HasValue AndAlso e.Button = MouseButtons.Left Then
            Dim d = SystemInformation.DragSize
            If Math.Abs(e.X - _downAt.Value.X) > d.Width OrElse Math.Abs(e.Y - _downAt.Value.Y) > d.Height Then
                _downAt = Nothing
                DoDragDrop(New DataObject(DataFormat, _actionType), DragDropEffects.Copy)
            End If
        End If
        MyBase.OnMouseMove(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        _downAt = Nothing
        MyBase.OnMouseUp(e)
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        Dim g = e.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        g.Clear(If(Parent?.BackColor, Theme.Bg))
        Theme.FillRound(g, If(_hover, Theme.AccentDim, Theme.Raised), New RectangleF(0, 0, Width - 1, Height - 1), 6)
        Dim glyph = Theme.GlyphFor(New ActionConfig With {.ActionType = _actionType})
        TextRenderer.DrawText(g, glyph, Theme.Icons(9.5F), New Rectangle(6, 0, 22, Height), Theme.Accent,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.HorizontalCenter Or TextFormatFlags.NoPrefix)
        TextRenderer.DrawText(g, Text, Font, New Rectangle(28, 0, Width - 30, Height), Theme.Fg,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.NoPrefix)
    End Sub
End Class

' ====================================================================== Druckvorlage

''' <summary>
''' Zeichnet die Belegung eines Profils als Vorlage für Keycap-Einleger: je Seite/Ordner ein Raster
''' 3 × 5 mit Feldern von 16 × 16 mm (Symbol + Beschriftung), mit Schnittlinien. Einheit: Millimeter.
''' </summary>
Public Module KeycapSheet
    Public Const CapMm As Single = 16.0F
    Public Const GapMm As Single = 3.0F
    Public Const MarginMm As Single = 12.0F
    Private Const Cols As Integer = 5
    Private Const Rows As Integer = 3
    Private Const TitleMm As Single = 7.0F

    ''' <summary>Höhe eines Seitenblocks (Titel + Raster) in mm.</summary>
    Public ReadOnly Property BlockHeightMm As Single
        Get
            Return TitleMm + Rows * CapMm + (Rows - 1) * GapMm + 8.0F
        End Get
    End Property

    Public ReadOnly Property BlockWidthMm As Single
        Get
            Return Cols * CapMm + (Cols - 1) * GapMm
        End Get
    End Property

    ''' <summary>Seiten, die gedruckt werden (alle, Ordner mit Kennzeichnung).</summary>
    Public Function PagesToPrint(profile As ProfileConfig) As List(Of PageConfig)
        Return profile.Pages.ToList()
    End Function

    ''' <summary>Zeichnet einen Seitenblock; g muss auf Millimeter stehen (PageUnit = Millimeter).</summary>
    Public Sub DrawBlock(g As Graphics, page As PageConfig, x As Single, y As Single)
        Using titleFont As New Font("Segoe UI Semibold", 9.0F, FontStyle.Regular, GraphicsUnit.Point)
            g.DrawString(If(page.IsFolder, "Ordner: ", "Seite: ") & page.Name, titleFont, Brushes.Black, x, y)
        End Using
        Dim top = y + TitleMm
        Using cut As New Pen(Color.FromArgb(150, 150, 150), 0.15F) With {.DashStyle = DashStyle.Dash},
              labelFont As New Font("Segoe UI", 6.0F, FontStyle.Regular, GraphicsUnit.Point),
              numFont As New Font("Segoe UI", 4.0F, FontStyle.Regular, GraphicsUnit.Point)
            For i = 0 To Rows * Cols - 1
                Dim cx = x + (i Mod Cols) * (CapMm + GapMm)
                Dim cy = top + (i \ Cols) * (CapMm + GapMm)
                Dim cell As New RectangleF(cx, cy, CapMm, CapMm)
                g.DrawRectangle(cut, cell.X, cell.Y, cell.Width, cell.Height)
                g.DrawString(i.ToString(), numFont, Brushes.Gray, cx + 0.5F, cy + 0.3F)
                DrawCap(g, page.GetKey(i), cell, labelFont)
            Next
        End Using
    End Sub

    Private Sub DrawCap(g As Graphics, key As KeyConfig, cell As RectangleF, font As Font)
        If key Is Nothing Then Return
        Dim label = If(key.Label, Theme.CaptionFor(key.Press))
        Dim img = KeyIcons.GetImage(key)
        If img IsNot Nothing Then
            Dim s = 8.5F
            g.InterpolationMode = InterpolationMode.HighQualityBicubic
            g.DrawImage(img, cell.X + (cell.Width - s) / 2, cell.Y + 1.8F, s, s)
        Else
            ' ohne Bild: Typ-Symbol aus Segoe MDL2 als Platzhalter
            Using glyphFont As New Font("Segoe MDL2 Assets", 14.0F, FontStyle.Regular, GraphicsUnit.Point)
                Using fmt As New StringFormat With {.Alignment = StringAlignment.Center}
                    g.DrawString(Theme.GlyphFor(key.Press), glyphFont, Brushes.Black, New RectangleF(cell.X, cell.Y + 1.8F, cell.Width, 8.0F), fmt)
                End Using
            End Using
        End If
        If String.IsNullOrEmpty(label) Then Return
        Dim textTop = cell.Y + 10.8F
        Using fmt As New StringFormat With {.Alignment = StringAlignment.Center, .Trimming = StringTrimming.EllipsisCharacter}
            g.DrawString(label, font, Brushes.Black, New RectangleF(cell.X + 0.5F, textTop, cell.Width - 1.0F, cell.Bottom - textTop - 0.3F), fmt)
        End Using
    End Sub

    ''' <summary>Wie viele Seitenblöcke passen untereinander auf ein Blatt der Höhe heightMm?</summary>
    Public Function BlocksPerSheet(heightMm As Single) As Integer
        Return Math.Max(1, CInt(Math.Floor((heightMm - 2 * MarginMm) / BlockHeightMm)))
    End Function

    ''' <summary>Alle Blöcke eines Profils als PNG (300 dpi), untereinander.</summary>
    Public Function RenderPng(profile As ProfileConfig, Optional dpi As Single = 300.0F) As Bitmap
        Dim pages = PagesToPrint(profile)
        Dim wMm = BlockWidthMm + 2 * MarginMm
        Dim hMm = pages.Count * BlockHeightMm + 2 * MarginMm
        Dim bmp As New Bitmap(CInt(wMm / 25.4F * dpi), CInt(hMm / 25.4F * dpi))
        bmp.SetResolution(dpi, dpi)
        Using g = Graphics.FromImage(bmp)
            g.Clear(Color.White)
            g.SmoothingMode = SmoothingMode.AntiAlias
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAlias
            g.PageUnit = GraphicsUnit.Millimeter
            For i = 0 To pages.Count - 1
                DrawBlock(g, pages(i), MarginMm, MarginMm + i * BlockHeightMm)
            Next
        End Using
        Return bmp
    End Function
End Module

''' <summary>Vorschau der Druckvorlage mit Drucken und Als-Bild-speichern.</summary>
Public Class KeycapSheetDialog
    Inherits ThemedForm

    Private ReadOnly _profile As ProfileConfig
    Private ReadOnly _doc As New PrintDocument()
    Private ReadOnly _preview As New PrintPreviewControl With {.Dock = DockStyle.Fill, .AutoZoom = True, .UseAntiAlias = True}
    Private _nextBlock As Integer

    Public Sub New(profile As ProfileConfig)
        _profile = profile
        Text = $"Druckvorlage – Profil {profile.Name}"
        StartPosition = FormStartPosition.CenterParent
        ShowInTaskbar = False
        MinimizeBox = False
        ClientSize = New Size(760, 820)
        MinimumSize = New Size(520, 500)

        _doc.DocumentName = Text
        _doc.DefaultPageSettings.Margins = New Margins(0, 0, 0, 0)
        AddHandler _doc.BeginPrint, Sub() _nextBlock = 0
        AddHandler _doc.PrintPage, AddressOf PrintPage
        _preview.Document = _doc

        Dim info As New Label With {.Text = "Felder 16 × 16 mm (Keycap-Einleger), gestrichelte Linien = Schnittkanten. Beim Drucken 'Tatsächliche Größe' / 100 % wählen.",
                                    .Dock = DockStyle.Top, .Height = 34, .ForeColor = Theme.Muted, .Padding = New Padding(16, 10, 16, 0)}
        Dim print As New ThemedButton("Drucken …", ButtonKind.Primary)
        Dim png As New ThemedButton("Als Bild speichern …", ButtonKind.Secondary)
        Dim close As New ThemedButton("Schließen", ButtonKind.Ghost) With {.DialogResult = DialogResult.Cancel}
        AddHandler print.Click, Sub() DoPrint()
        AddHandler png.Click, Sub() SavePng()

        Dim host As New Panel With {.Dock = DockStyle.Fill, .Padding = New Padding(16, 8, 16, 8)}
        host.Controls.Add(_preview)
        Controls.Add(host)
        Controls.Add(info)
        Controls.Add(MakeFooter(Nothing, close, png, print))
        CancelButton = close
        Theme.Apply(Me)
    End Sub

    Private Sub PrintPage(sender As Object, e As PrintPageEventArgs)
        Dim pages = KeycapSheet.PagesToPrint(_profile)
        Dim g = e.Graphics
        g.PageUnit = GraphicsUnit.Millimeter
        g.SmoothingMode = SmoothingMode.AntiAlias
        ' Druckränder des Geräts ausgleichen, damit die Lage stimmt (Größe ist über Millimeter ohnehin exakt)
        Dim hardX = e.PageSettings.HardMarginX / 100.0F * 25.4F
        Dim hardY = e.PageSettings.HardMarginY / 100.0F * 25.4F
        Dim sheetH = e.PageSettings.PaperSize.Height / 100.0F * 25.4F
        Dim perSheet = KeycapSheet.BlocksPerSheet(sheetH)
        Dim y = KeycapSheet.MarginMm - hardY
        Dim n = 0
        While _nextBlock < pages.Count AndAlso n < perSheet
            KeycapSheet.DrawBlock(g, pages(_nextBlock), KeycapSheet.MarginMm - hardX, y)
            y += KeycapSheet.BlockHeightMm
            _nextBlock += 1
            n += 1
        End While
        e.HasMorePages = _nextBlock < pages.Count
    End Sub

    Private Sub DoPrint()
        Using d As New PrintDialog With {.Document = _doc, .UseEXDialog = True}
            If d.ShowDialog(Me) = DialogResult.OK Then
                Try
                    _doc.Print()
                Catch ex As Exception
                    MessageBox.Show(Me, ex.Message, "Drucken", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End Using
    End Sub

    Private Sub SavePng()
        Using d As New SaveFileDialog With {.Filter = "PNG-Bild (*.png)|*.png", .FileName = $"Keycaps-{_profile.Name}.png"}
            If d.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using bmp = KeycapSheet.RenderPng(_profile)
                    bmp.Save(d.FileName, Imaging.ImageFormat.Png)
                End Using
            Catch ex As Exception
                MessageBox.Show(Me, ex.Message, "Als Bild speichern", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then _doc.Dispose()
        MyBase.Dispose(disposing)
    End Sub
End Class
