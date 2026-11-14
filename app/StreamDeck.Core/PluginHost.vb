' Plugin-Schnittstelle für eigene Aktionen. Aktion: { "type": "plugin", "plugin": "<Name>", "args": "…" }
'
' Zwei Arten von Plugins im Ordner %APPDATA%\StreamDeck\plugins:
'  - DLL (.NET 8): öffentliche Klasse mit parameterlosem Konstruktor, die IDeckPlugin implementiert
'    (Verweis auf StreamDeck.Core.dll). Name = IDeckPlugin.Name.
'  - Skript/Programm: <Name>.ps1, .cmd, .bat, .py oder .exe. Wird versteckt gestartet, "args" wird als
'    Argumentzeile übergeben; Umgebungsvariable STREAMDECK_KEY enthält die Tastennummer.
' Achtung: Plugins laufen mit den Rechten der App – nur vertrauenswürdige Dateien in den Ordner legen.
Imports System.Diagnostics
Imports System.Reflection

Public Interface IDeckPlugin
    ''' <summary>Name, unter dem das Plugin in der Aktion angesprochen wird.</summary>
    ReadOnly Property Name As String
    ''' <summary>Kurzbeschreibung für die Auswahl im Editor.</summary>
    ReadOnly Property Description As String
    Function ExecuteAsync(args As String, context As ActionContext) As Task
End Interface

Public Class PluginHost
    Implements IActionHandler

    Public Shared ReadOnly ScriptExtensions As String() = {".ps1", ".cmd", ".bat", ".py", ".exe"}

    Private ReadOnly _plugins As New Dictionary(Of String, IDeckPlugin)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _log As Action(Of String)

    Public ReadOnly Property Folder As String

    Public Shared ReadOnly Property DefaultFolder As String
        Get
            Return IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamDeck", "plugins")
        End Get
    End Property

    Public Sub New(folder As String, log As Action(Of String))
        Me.Folder = folder
        _log = log
    End Sub

    ''' <summary>Lädt alle DLL-Plugins aus dem Ordner (Fehler landen im Log). Liefert die Anzahl.</summary>
    Public Function LoadDlls() As Integer
        _plugins.Clear()
        If Not IO.Directory.Exists(Folder) Then Return 0
        For Each dll In IO.Directory.GetFiles(Folder, "*.dll")
            Try
                Dim asm = Assembly.LoadFrom(dll)
                For Each t In asm.GetExportedTypes()
                    If t.IsAbstract OrElse Not GetType(IDeckPlugin).IsAssignableFrom(t) OrElse t.GetConstructor(Type.EmptyTypes) Is Nothing Then Continue For
                    Add(DirectCast(Activator.CreateInstance(t), IDeckPlugin))
                Next
            Catch ex As Exception
                _log($"Plugin {IO.Path.GetFileName(dll)} konnte nicht geladen werden: {ex.Message}")
            End Try
        Next
        If _plugins.Count > 0 Then _log($"Plugins geladen: {String.Join(", ", _plugins.Keys)}")
        Return _plugins.Count
    End Function

    ''' <summary>Plugin direkt hinzufügen (für Tests und eingebaute Erweiterungen).</summary>
    Public Sub Add(plugin As IDeckPlugin)
        If plugin Is Nothing OrElse String.IsNullOrWhiteSpace(plugin.Name) Then Return
        _plugins(plugin.Name.Trim()) = plugin
    End Sub

    ''' <summary>Alle verfügbaren Plugin-Namen (DLL-Plugins und Skripte) mit Beschreibung, alphabetisch.</summary>
    Public Function Available() As List(Of (Name As String, Description As String))
        Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        For Each p In _plugins.Values
            result(p.Name) = If(p.Description, "")
        Next
        If IO.Directory.Exists(Folder) Then
            For Each f In IO.Directory.GetFiles(Folder)
                If Not ScriptExtensions.Contains(IO.Path.GetExtension(f).ToLowerInvariant()) Then Continue For
                Dim n = IO.Path.GetFileNameWithoutExtension(f)
                If Not result.ContainsKey(n) Then result(n) = "Skript " & IO.Path.GetFileName(f)
            Next
        End If
        Return result.OrderBy(Function(kv) kv.Key, StringComparer.CurrentCultureIgnoreCase).Select(Function(kv) (kv.Key, kv.Value)).ToList()
    End Function

    ''' <summary>Skriptdatei zum Namen (erste passende Endung in der Reihenfolge von ScriptExtensions) oder Nothing.</summary>
    Public Function FindScript(name As String) As String
        If String.IsNullOrWhiteSpace(name) OrElse name.IndexOfAny(IO.Path.GetInvalidFileNameChars()) >= 0 Then Return Nothing
        For Each ext In ScriptExtensions
            Dim p = IO.Path.Combine(Folder, name.Trim() & ext)
            If IO.File.Exists(p) Then Return p
        Next
        Return Nothing
    End Function

    ''' <summary>Startinfo für ein Skript-Plugin (versteckt, Ausgabe umgeleitet).</summary>
    Public Shared Function BuildScriptStart(path As String, args As String, keyIndex As Integer) As ProcessStartInfo
        Dim a = If(args, "").Trim()
        Dim tail = If(a.Length > 0, " " & a, "")
        Dim psi As ProcessStartInfo
        Select Case IO.Path.GetExtension(path).ToLowerInvariant()
            Case ".ps1"
                psi = New ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File ""{path}""{tail}")
            Case ".cmd", ".bat"
                ' cmd /c ""Pfad" Argumente" – äußere Anführungszeichen entfernt cmd selbst
                psi = New ProcessStartInfo("cmd.exe", $"/c """"{path}""{tail}""")
            Case ".py"
                psi = New ProcessStartInfo("py.exe", $"""{path}""{tail}")
            Case Else
                psi = New ProcessStartInfo(path, a)
        End Select
        psi.UseShellExecute = False
        psi.CreateNoWindow = True
        psi.RedirectStandardError = True
        psi.RedirectStandardOutput = True
        psi.WorkingDirectory = IO.Path.GetDirectoryName(path)
        psi.Environment("STREAMDECK_KEY") = keyIndex.ToString(Globalization.CultureInfo.InvariantCulture)
        Return psi
    End Function

    Public Async Function ExecuteAsync(action As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Dim name = If(action.Plugin, "").Trim()
        If name.Length = 0 Then Throw New ArgumentException("Kein Plugin angegeben.")
        Dim plugin As IDeckPlugin = Nothing
        If _plugins.TryGetValue(name, plugin) Then
            Await plugin.ExecuteAsync(action.Args, context).ConfigureAwait(False)
            Return
        End If
        Dim script = FindScript(name)
        If script Is Nothing Then Throw New IO.FileNotFoundException($"Plugin '{name}' nicht gefunden (Ordner {Folder}).")
        Using p = Process.Start(BuildScriptStart(script, action.Args, context.KeyIndex))
            Dim errTask = p.StandardError.ReadToEndAsync()
            Dim outTask = p.StandardOutput.ReadToEndAsync()
            Await p.WaitForExitAsync(context.Cancellation).ConfigureAwait(False)
            Dim err = (Await errTask.ConfigureAwait(False)).Trim()
            Await outTask.ConfigureAwait(False)
            If p.ExitCode <> 0 Then
                Dim first = err.Split({vbCr, vbLf}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                Throw New InvalidOperationException($"Plugin '{name}' beendet mit Code {p.ExitCode}{If(first Is Nothing, "", ": " & first)}")
            End If
        End Using
    End Function
End Class
