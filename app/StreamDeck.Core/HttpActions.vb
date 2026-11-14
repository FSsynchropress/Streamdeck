' Aktionstyp http: GET/POST/… im Hintergrund, z. B. für Webhooks (n8n, Home Assistant, Hue).
' Die Anfrage wird in BuildRequest gebaut, damit sie sich testen lässt.
Imports System.Net.Http
Imports System.Net.Http.Headers

Public Module HttpActions
    Public Const DefaultContentType As String = "application/json"
    Public ReadOnly Timeout As TimeSpan = TimeSpan.FromSeconds(15)

    Private ReadOnly Client As New HttpClient With {.Timeout = Timeout}

    Public Function BuildRequest(a As ActionConfig) As HttpRequestMessage
        Dim url = ProcessActions.NormalizeUrl(a.Url)
        Dim methodName = If(String.IsNullOrWhiteSpace(a.Method), "GET", a.Method.Trim().ToUpperInvariant())
        If Not ActionTypes.HttpMethods.Contains(methodName) Then Throw New ArgumentException($"Unbekannte HTTP-Methode '{a.Method}'.")
        Dim req As New HttpRequestMessage(New HttpMethod(methodName), url)

        If methodName <> "GET" AndAlso methodName <> "DELETE" AndAlso a.Body IsNot Nothing Then
            Dim ct = If(String.IsNullOrWhiteSpace(a.ContentType), DefaultContentType, a.ContentType.Trim())
            Dim parsed As MediaTypeHeaderValue = Nothing
            If Not MediaTypeHeaderValue.TryParse(ct, parsed) Then Throw New ArgumentException($"Ungültiger Inhaltstyp '{ct}'.")
            If parsed.CharSet Is Nothing Then parsed.CharSet = "utf-8"
            Dim content As New StringContent(a.Body, Text.Encoding.UTF8)
            content.Headers.ContentType = parsed
            req.Content = content
        End If

        If a.Headers IsNot Nothing Then
            For Each kv In a.Headers
                If String.IsNullOrWhiteSpace(kv.Key) Then Continue For
                Dim name = kv.Key.Trim()
                If Not req.Headers.TryAddWithoutValidation(name, If(kv.Value, "")) Then
                    ' Inhalts-Kopfzeilen (z. B. Content-Language) gehören an den Inhalt
                    If req.Content Is Nothing OrElse Not req.Content.Headers.TryAddWithoutValidation(name, If(kv.Value, "")) Then
                        Throw New ArgumentException($"Kopfzeile '{name}' ist hier nicht erlaubt.")
                    End If
                End If
            Next
        End If
        Return req
    End Function

    ''' <summary>
    ''' "Name: Wert" je Zeile -> Wörterbuch (für das Eingabefeld im Editor). Leere Zeilen werden übersprungen.
    ''' Wirft FormatException bei Zeilen ohne Doppelpunkt.
    ''' </summary>
    Public Function ParseHeaders(text As String) As Dictionary(Of String, String)
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        If String.IsNullOrWhiteSpace(text) Then Return result
        For Each raw In text.Split({vbCrLf, vbLf}, StringSplitOptions.None)
            Dim line = raw.Trim()
            If line.Length = 0 Then Continue For
            Dim i = line.IndexOf(":"c)
            If i <= 0 Then Throw New FormatException($"Kopfzeile '{line}' hat kein 'Name: Wert'-Format.")
            result(line.Substring(0, i).Trim()) = line.Substring(i + 1).Trim()
        Next
        Return result
    End Function

    Public Function FormatHeaders(headers As Dictionary(Of String, String)) As String
        If headers Is Nothing Then Return ""
        Return String.Join(vbCrLf, headers.Select(Function(kv) $"{kv.Key}: {kv.Value}"))
    End Function

    ''' <summary>Registriert http beim Runner. Antworten ohne 2xx-Status gelten als Fehler und landen im Log.</summary>
    Public Sub Register(runner As ActionRunner)
        runner.Register(ActionTypes.Http, New DelegateActionHandler(
            Async Function(a, ctx)
                Using req = BuildRequest(a)
                    Using resp = Await Client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ctx.Cancellation).ConfigureAwait(False)
                        If Not resp.IsSuccessStatusCode Then
                            Throw New HttpRequestException($"Server antwortet {CInt(resp.StatusCode)} {resp.ReasonPhrase}")
                        End If
                    End Using
                End Using
            End Function))
    End Sub
End Module
