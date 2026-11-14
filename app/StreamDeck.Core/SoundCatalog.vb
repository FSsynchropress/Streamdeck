' Katalog der mitgelieferten Sounds (Sounds\katalog.json): Anzeigename, Kategorie und Symbol je Datei.
' Dateien ohne Katalogeintrag landen unter „Sonstige“, damit eigene Ergänzungen im Ordner sichtbar bleiben.
Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

Public Class SoundEntry
    ''' <summary>Pfad wie in der Konfiguration, z. B. "Sounds\airhorn.mp3".</summary>
    Public Property File As String
    Public Property Name As String
    Public Property Category As String
    Public Property Icon As String
End Class

Public Class SoundCatalog
    Public Const FileName As String = "katalog.json"
    Public Const OtherCategory As String = "Sonstige"
    Public Shared ReadOnly Extensions As String() = {".mp3", ".wav"}

    Public Property Categories As New List(Of String)
    Public Property Sounds As New List(Of SoundEntry)

    Private Class CatalogFile
        <JsonPropertyName("categories")> Public Property Categories As List(Of String)
        <JsonPropertyName("sounds")> Public Property Sounds As List(Of CatalogItem)
    End Class

    Private Class CatalogItem
        <JsonPropertyName("file")> Public Property File As String
        <JsonPropertyName("name")> Public Property Name As String
        <JsonPropertyName("category")> Public Property Category As String
        <JsonPropertyName("icon")> Public Property Icon As String
    End Class

    ''' <summary>Liest den Ordner und (falls vorhanden) den Katalog. Einträge ohne Datei werden übergangen.</summary>
    ''' <param name="folder">Absoluter Pfad des Sound-Ordners.</param>
    ''' <param name="relativePrefix">Präfix für <see cref="SoundEntry.File"/>, z. B. "Sounds".</param>
    Public Shared Function Load(folder As String, relativePrefix As String) As SoundCatalog
        Dim result As New SoundCatalog
        If Not Directory.Exists(folder) Then Return result

        Dim files = Directory.GetFiles(folder).
            Where(Function(f) Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())).
            Select(Function(f) Path.GetFileName(f)).
            ToHashSet(StringComparer.OrdinalIgnoreCase)

        Dim parsed As CatalogFile = Nothing
        Dim catalogPath = Path.Combine(folder, FileName)
        If IO.File.Exists(catalogPath) Then
            Try
                parsed = JsonSerializer.Deserialize(Of CatalogFile)(IO.File.ReadAllText(catalogPath))
            Catch ex As JsonException
                parsed = Nothing     ' kaputter Katalog: Dateien trotzdem zeigen
            End Try
        End If

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If parsed IsNot Nothing Then
            If parsed.Categories IsNot Nothing Then result.Categories.AddRange(parsed.Categories.Where(Function(c) Not String.IsNullOrWhiteSpace(c)))
            For Each item In If(parsed.Sounds, New List(Of CatalogItem))
                If String.IsNullOrWhiteSpace(item?.File) OrElse Not files.Contains(item.File) OrElse Not seen.Add(item.File) Then Continue For
                Dim cat = If(String.IsNullOrWhiteSpace(item.Category), OtherCategory, item.Category)
                result.Sounds.Add(New SoundEntry With {
                    .File = Path.Combine(relativePrefix, item.File),
                    .Name = If(String.IsNullOrWhiteSpace(item.Name), NameFromFile(item.File), item.Name),
                    .Category = cat,
                    .Icon = item.Icon})
                If Not result.Categories.Contains(cat) Then result.Categories.Add(cat)
            Next
        End If

        Dim rest = files.Where(Function(f) Not seen.Contains(f)).OrderBy(Function(f) f, StringComparer.CurrentCultureIgnoreCase).ToList()
        For Each f In rest
            result.Sounds.Add(New SoundEntry With {.File = Path.Combine(relativePrefix, f), .Name = NameFromFile(f), .Category = OtherCategory})
        Next
        If rest.Count > 0 AndAlso Not result.Categories.Contains(OtherCategory) Then result.Categories.Add(OtherCategory)
        ' Kategorien ohne Sounds ausblenden
        result.Categories = result.Categories.Where(Function(c) result.Sounds.Any(Function(s) s.Category = c)).ToList()
        Return result
    End Function

    ''' <summary>"tuer-knarren.mp3" → "Tuer knarren".</summary>
    Public Shared Function NameFromFile(file As String) As String
        Dim n = Path.GetFileNameWithoutExtension(file).Replace("-"c, " "c).Replace("_"c, " "c).Trim()
        If n.Length = 0 Then Return file
        Return Char.ToUpper(n(0)) & n.Substring(1)
    End Function

    ''' <summary>Filtert nach Kategorie (Nothing = alle) und Suchtext (Name, Kategorie oder Dateiname; Umlaute egal).</summary>
    Public Function Filter(category As String, query As String) As List(Of SoundEntry)
        Dim words = Normalize(If(query, "")).Split(" "c, StringSplitOptions.RemoveEmptyEntries)
        Return Sounds.Where(Function(s)
                                If category IsNot Nothing AndAlso s.Category <> category Then Return False
                                Dim hay = Normalize(s.Name & " " & s.Category & " " & Path.GetFileNameWithoutExtension(s.File))
                                Return words.All(Function(w) hay.Contains(w))
                            End Function).ToList()
    End Function

    Private Shared Function Normalize(s As String) As String
        Return s.ToLowerInvariant().Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss").Replace("-"c, " "c)
    End Function
End Class
