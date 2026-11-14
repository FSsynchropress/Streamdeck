' Protokoll in Datei für die Fehlersuche: %APPDATA%\StreamDeck\logs\streamdeck-JJJJ-MM-TT.log
' Eine Datei pro Tag, ältere als KeepDays werden beim Start gelöscht. Thread-sicher; Schreibfehler
' werden verschluckt (das Protokoll darf die App nie stören).
Imports System.IO

Public Class FileLog
    Public Const KeepDays As Integer = 14
    Private Const Prefix As String = "streamdeck-"

    Private ReadOnly _sync As New Object
    Private ReadOnly _clock As Func(Of Date)

    Public ReadOnly Property Folder As String

    Public Shared ReadOnly Property DefaultFolder As String
        Get
            Return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StreamDeck", "logs")
        End Get
    End Property

    Public Sub New(folder As String, Optional clock As Func(Of Date) = Nothing)
        Me.Folder = folder
        _clock = If(clock, Function() Date.Now)
        Try
            Directory.CreateDirectory(folder)
            DeleteOld()
        Catch
        End Try
    End Sub

    Public Function FileFor(day As Date) As String
        Return Path.Combine(Folder, $"{Prefix}{day:yyyy-MM-dd}.log")
    End Function

    Public Sub Write(message As String)
        Dim now = _clock()
        Dim line = $"{now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}"
        SyncLock _sync
            Try
                File.AppendAllText(FileFor(now), line, Text.Encoding.UTF8)
            Catch
            End Try
        End SyncLock
    End Sub

    Private Sub DeleteOld()
        Dim limit = _clock().Date.AddDays(-KeepDays)
        For Each f In Directory.GetFiles(Folder, Prefix & "*.log")
            Dim name = Path.GetFileNameWithoutExtension(f).Substring(Prefix.Length)
            Dim day As Date
            If Date.TryParseExact(name, "yyyy-MM-dd", Globalization.CultureInfo.InvariantCulture, Globalization.DateTimeStyles.None, day) AndAlso day < limit Then
                Try
                    File.Delete(f)
                Catch
                End Try
            End If
        Next
    End Sub
End Class
