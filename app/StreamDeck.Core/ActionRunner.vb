' Führt Aktionen asynchron aus. Die Windows-spezifischen Aktionstypen (hotkey, sound, obs …)
' registriert die App als Handler; multi und wait sind hier eingebaut.
Imports System.Threading

Public Interface IActionHandler
    Function ExecuteAsync(action As ActionConfig, context As ActionContext) As Task
End Interface

Public Class ActionContext
    ''' <summary>Taste, die die Aktion ausgelöst hat (-1 = z. B. Test-Knopf im Editor).</summary>
    Public Property KeyIndex As Integer = -1
    Public Property Cancellation As CancellationToken
    Friend Property Depth As Integer
End Class

''' <summary>Handler aus einer Funktion (praktisch für einfache Typen und Tests).</summary>
Public Class DelegateActionHandler
    Implements IActionHandler

    Private ReadOnly _fn As Func(Of ActionConfig, ActionContext, Task)

    Public Sub New(fn As Func(Of ActionConfig, ActionContext, Task))
        _fn = fn
    End Sub

    Public Shared Function FromSync(action As Action(Of ActionConfig)) As DelegateActionHandler
        Return New DelegateActionHandler(Function(a, c)
                                             action(a)
                                             Return Task.CompletedTask
                                         End Function)
    End Function

    Public Function ExecuteAsync(action As ActionConfig, context As ActionContext) As Task Implements IActionHandler.ExecuteAsync
        Return _fn(action, context)
    End Function
End Class

Public Class ActionRunner
    Public Const MaxNesting As Integer = 8
    Public Const MaxWaitMs As Integer = 60000

    Private ReadOnly _handlers As New Dictionary(Of String, IActionHandler)(StringComparer.OrdinalIgnoreCase)

    Public Event Log(message As String)

    Public Sub New()
        _handlers(ActionTypes.Multi) = New DelegateActionHandler(AddressOf RunMultiAsync)
        _handlers(ActionTypes.Wait) = New DelegateActionHandler(
            Function(a, c) Task.Delay(Math.Max(0, Math.Min(MaxWaitMs, If(a.Ms, 0))), c.Cancellation))
    End Sub

    Public Sub Register(actionType As String, handler As IActionHandler)
        _handlers(actionType) = handler
    End Sub

    Public Function CanHandle(actionType As String) As Boolean
        Return actionType IsNot Nothing AndAlso _handlers.ContainsKey(actionType)
    End Function

    ''' <summary>Startet die Aktion im Hintergrund (blockiert nie). Fehler landen im Log.</summary>
    Public Function Fire(action As ActionConfig, Optional keyIndex As Integer = -1) As Task
        If action Is Nothing Then Return Task.CompletedTask
        Dim ctx As New ActionContext With {.KeyIndex = keyIndex, .Cancellation = CancellationToken.None}
        Return Task.Run(Function() RunAsync(action, ctx))
    End Function

    ''' <summary>Führt die Aktion aus und wartet darauf. Wirft nie – Fehler werden gemeldet.</summary>
    Public Async Function RunAsync(action As ActionConfig, context As ActionContext) As Task
        If action Is Nothing OrElse String.IsNullOrWhiteSpace(action.ActionType) Then Return
        Dim handler As IActionHandler = Nothing
        If Not _handlers.TryGetValue(action.ActionType.Trim(), handler) Then
            RaiseEvent Log($"Unbekannter Aktionstyp '{action.ActionType}'.")
            Return
        End If
        Try
            Await handler.ExecuteAsync(action, context).ConfigureAwait(False)
        Catch ex As OperationCanceledException
            ' abgebrochen – kein Fehler
        Catch ex As Exception
            Dim who = If(context.KeyIndex >= 0, $"Taste {context.KeyIndex}", "Test")
            RaiseEvent Log($"{who}: {ActionTypes.Describe(action)} fehlgeschlagen – {ex.Message}")
        End Try
    End Function

    Private Async Function RunMultiAsync(action As ActionConfig, context As ActionContext) As Task
        If action.Actions Is Nothing Then Return
        If context.Depth >= MaxNesting Then Throw New InvalidOperationException("zu tief verschachtelte Mehrfach-Aktion")
        Dim inner As New ActionContext With {.KeyIndex = context.KeyIndex, .Cancellation = context.Cancellation, .Depth = context.Depth + 1}
        For Each a In action.Actions.ToList()
            context.Cancellation.ThrowIfCancellationRequested()
            Await RunAsync(a, inner).ConfigureAwait(False)
        Next
    End Function
End Class
