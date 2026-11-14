' Aktionen, die Prozesse starten: launch (Programm/Datei/Ordner), url (Browser), command (cmd/PowerShell).
' Die ProcessStartInfo wird in eigenen Funktionen gebaut, damit sie sich testen lässt.
Imports System.Diagnostics

Public Module ProcessActions
    Public Function BuildLaunch(a As ActionConfig) As ProcessStartInfo
        If String.IsNullOrWhiteSpace(a.Path) Then Throw New ArgumentException("Kein Programm/Pfad angegeben.")
        Dim path = Environment.ExpandEnvironmentVariables(a.Path.Trim().Trim(""""c))
        Dim psi As New ProcessStartInfo(path) With {.UseShellExecute = True}
        If Not String.IsNullOrWhiteSpace(a.Args) Then psi.Arguments = Environment.ExpandEnvironmentVariables(a.Args)
        ' Programme mit vollem Pfad im eigenen Ordner starten (manche Programme erwarten das)
        If IO.Path.IsPathRooted(path) AndAlso IO.File.Exists(path) Then psi.WorkingDirectory = IO.Path.GetDirectoryName(path)
        Return psi
    End Function

    ''' <summary>
    ''' Pfad für Dateien, die zur App gehören (z. B. mitgelieferte Sounds): Relative Pfade wie
    ''' "Sounds\tusch.mp3" beziehen sich auf den Programmordner, damit die Belegung auch nach dem
    ''' Verschieben der App funktioniert. Umgebungsvariablen werden aufgelöst.
    ''' </summary>
    Public Function ResolveAppRelativePath(path As String, baseDir As String) As String
        Dim p = Environment.ExpandEnvironmentVariables(path.Trim().Trim(""""c))
        If IO.Path.IsPathRooted(p) Then Return IO.Path.GetFullPath(p)
        Return IO.Path.GetFullPath(IO.Path.Combine(baseDir, p))
    End Function

    ''' <summary>Ohne Schema wird https:// ergänzt. Nur http(s) ist erlaubt.</summary>
    Public Function NormalizeUrl(url As String) As String
        If String.IsNullOrWhiteSpace(url) Then Throw New ArgumentException("Keine Adresse angegeben.")
        Dim s = url.Trim()
        If Not s.Contains("://") Then s = "https://" & s
        Dim uri As Uri = Nothing
        If Not Uri.TryCreate(s, UriKind.Absolute, uri) OrElse (uri.Scheme <> Uri.UriSchemeHttp AndAlso uri.Scheme <> Uri.UriSchemeHttps) Then
            Throw New ArgumentException($"Ungültige Webadresse '{url}'.")
        End If
        Return uri.AbsoluteUri
    End Function

    Public Function BuildUrl(a As ActionConfig) As ProcessStartInfo
        Dim url = NormalizeUrl(a.Url)
        If String.IsNullOrWhiteSpace(a.Browser) Then Return New ProcessStartInfo(url) With {.UseShellExecute = True}
        ' Gewählter Browser: "firefox" -> "firefox.exe" (ShellExecute findet ihn über App Paths), sonst voller Pfad
        Dim browser = Environment.ExpandEnvironmentVariables(a.Browser.Trim().Trim(""""c))
        If Not IO.Path.IsPathRooted(browser) AndAlso String.IsNullOrEmpty(IO.Path.GetExtension(browser)) Then browser &= ".exe"
        Return New ProcessStartInfo(browser, """" & url & """") With {.UseShellExecute = True}
    End Function

    ''' <summary>"C:\Programme\OBS\obs64.exe" / "obs64.exe" / "obs64" -> "obs64"</summary>
    Public Function ProcessNameFromPath(path As String) As String
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("Kein Programm angegeben.")
        Dim p = Environment.ExpandEnvironmentVariables(path.Trim().Trim(""""c))
        Dim name = IO.Path.GetFileName(p)
        If name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) Then name = name.Substring(0, name.Length - 4)
        If name.Length = 0 Then Throw New ArgumentException($"'{path}' ist kein Programm.")
        Return name
    End Function

    ''' <summary>
    ''' Schließt alle Prozesse des Programms: höflich über das Hauptfenster (wie Klick auf X) oder
    ''' mit force sofort (Kill, ungespeicherte Daten gehen verloren). Liefert die Anzahl der Prozesse.
    ''' </summary>
    Public Function CloseProcesses(a As ActionConfig, log As Action(Of String)) As Integer
        Dim name = ProcessNameFromPath(a.Path)
        Dim force = a.Force.GetValueOrDefault()
        Dim procs = Process.GetProcessesByName(name)
        Dim count = 0
        Try
            If procs.Length = 0 Then
                log($"'{name}' läuft nicht.")
                Return 0
            End If
            For Each p In procs
                Try
                    If force Then
                        p.Kill()
                        count += 1
                    ElseIf p.CloseMainWindow() Then
                        count += 1
                    End If
                Catch ex As InvalidOperationException
                    ' inzwischen schon beendet
                Catch ex As ComponentModel.Win32Exception
                    log($"'{name}' (PID {p.Id}): {ex.Message} – läuft es als Administrator?")
                End Try
            Next
            If count = 0 AndAlso Not force Then log($"'{name}' hat kein Fenster zum Schließen – 'Erzwingen' verwenden.")
            Return count
        Finally
            For Each p In procs
                p.Dispose()
            Next
        End Try
    End Function

    Public Function IsPowerShell(a As ActionConfig) As Boolean
        Return a.Shell IsNot Nothing AndAlso (a.Shell.Equals("powershell", StringComparison.OrdinalIgnoreCase) OrElse a.Shell.Equals("pwsh", StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>
    ''' cmd: versteckt "cmd /c …", sichtbar "cmd /k …" (Fenster bleibt offen, damit man die Ausgabe sieht).
    ''' PowerShell: Endet der Befehl auf .ps1, wird das Skript mit -File gestartet, sonst der Text als
    ''' -EncodedCommand (damit Anführungszeichen usw. nicht verfälscht werden). Sichtbar mit -NoExit.
    ''' </summary>
    Public Function BuildCommand(a As ActionConfig) As ProcessStartInfo
        If String.IsNullOrWhiteSpace(a.Command) Then Throw New ArgumentException("Kein Befehl angegeben.")
        Dim hidden = a.Hidden.GetValueOrDefault(False)
        Dim psi As ProcessStartInfo

        If IsPowerShell(a) Then
            psi = New ProcessStartInfo(If(a.Shell.Equals("pwsh", StringComparison.OrdinalIgnoreCase), "pwsh.exe", "powershell.exe"))
            psi.ArgumentList.Add("-NoProfile")
            psi.ArgumentList.Add("-ExecutionPolicy")
            psi.ArgumentList.Add("Bypass")
            If Not hidden Then psi.ArgumentList.Add("-NoExit")
            Dim cmd = a.Command.Trim()
            Dim script = cmd.Trim(""""c)
            If script.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase) AndAlso Not script.Contains(""""c) Then
                psi.ArgumentList.Add("-File")
                psi.ArgumentList.Add(Environment.ExpandEnvironmentVariables(script))
            Else
                psi.ArgumentList.Add("-EncodedCommand")
                psi.ArgumentList.Add(Convert.ToBase64String(Text.Encoding.Unicode.GetBytes(cmd)))
            End If
        Else
            psi = New ProcessStartInfo("cmd.exe", If(hidden, "/c ", "/k ") & a.Command.Trim())
        End If

        If hidden Then
            psi.UseShellExecute = False
            psi.CreateNoWindow = True
            psi.WindowStyle = ProcessWindowStyle.Hidden
            psi.RedirectStandardError = True
            psi.RedirectStandardOutput = True
        Else
            psi.UseShellExecute = True
        End If
        Return psi
    End Function

    ''' <summary>Registriert launch, close, url und command beim Runner.</summary>
    Public Sub Register(runner As ActionRunner, log As Action(Of String))
        runner.Register(ActionTypes.Launch, DelegateActionHandler.FromSync(Sub(a) StartDetached(BuildLaunch(a))))
        runner.Register(ActionTypes.Close, DelegateActionHandler.FromSync(Sub(a) CloseProcesses(a, log)))
        runner.Register(ActionTypes.Url, DelegateActionHandler.FromSync(Sub(a) StartDetached(BuildUrl(a))))
        runner.Register(ActionTypes.Command, New DelegateActionHandler(
            Async Function(a, ctx)
                Dim psi = BuildCommand(a)
                If psi.UseShellExecute Then
                    StartDetached(psi)
                    Return
                End If
                ' Versteckt: Ausgabe einsammeln und bei Fehlern ins Log schreiben
                Using p = Process.Start(psi)
                    Dim errTask = p.StandardError.ReadToEndAsync()
                    Dim outTask = p.StandardOutput.ReadToEndAsync()
                    Await p.WaitForExitAsync(ctx.Cancellation).ConfigureAwait(False)
                    Dim err = (Await errTask.ConfigureAwait(False)).Trim()
                    Await outTask.ConfigureAwait(False)
                    If p.ExitCode <> 0 Then
                        Dim first = err.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                        log($"Befehl beendet mit Code {p.ExitCode}{If(first Is Nothing, "", ": " & first)}")
                    End If
                End Using
            End Function))
    End Sub

    Private Sub StartDetached(psi As ProcessStartInfo)
        Process.Start(psi)?.Dispose()
    End Sub
End Module
