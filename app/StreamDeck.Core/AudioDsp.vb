' Kleine Audio-Bausteine für den Sprach-Mix (ohne NAudio, damit sie testbar sind).

''' <summary>
''' Rauschsperre (Noise Gate): Liegt der Pegel unter der Schwelle, wird das Signal nach einer kurzen
''' Haltezeit sanft ausgeblendet; beim Sprechen sofort wieder eingeblendet. Entfernt Rauschen in den
''' Sprechpausen, nicht während man spricht.
''' </summary>
Public Class NoiseGate
    Private ReadOnly _channels As Integer
    Private ReadOnly _threshold As Single
    Private ReadOnly _holdFrames As Integer
    Private ReadOnly _attack As Single
    Private ReadOnly _release As Single
    Private ReadOnly _floor As Single
    Private _hold As Integer
    Private _gain As Single

    Public Sub New(sampleRate As Integer, channels As Integer, thresholdDb As Double,
                   Optional holdMs As Double = 200, Optional attackMs As Double = 3, Optional releaseMs As Double = 120,
                   Optional floorDb As Double = -60)
        _channels = Math.Max(1, channels)
        _threshold = CSng(DbToLinear(thresholdDb))
        _holdFrames = CInt(holdMs * sampleRate / 1000)
        _attack = Coefficient(attackMs, sampleRate)
        _release = Coefficient(releaseMs, sampleRate)
        _floor = CSng(DbToLinear(floorDb))
        _gain = _floor
    End Sub

    Public Shared Function DbToLinear(db As Double) As Double
        Return Math.Pow(10, db / 20)
    End Function

    Private Shared Function Coefficient(ms As Double, sampleRate As Integer) As Single
        Return CSng(1 - Math.Exp(-1 / Math.Max(1, ms * sampleRate / 1000)))
    End Function

    ''' <summary>Aktuelle Verstärkung 0…1 (für Tests).</summary>
    Public ReadOnly Property Gain As Single
        Get
            Return _gain
        End Get
    End Property

    Public Sub Process(buffer As Single(), offset As Integer, count As Integer)
        Dim i = offset
        Dim last = offset + count - _channels
        While i <= last
            Dim peak = 0.0F
            For c = 0 To _channels - 1
                Dim v = Math.Abs(buffer(i + c))
                If v > peak Then peak = v
            Next
            If peak >= _threshold Then _hold = _holdFrames
            Dim target As Single
            If _hold > 0 Then
                target = 1.0F
                _hold -= 1
            Else
                target = _floor
            End If
            _gain += (target - _gain) * If(target > _gain, _attack, _release)
            For c = 0 To _channels - 1
                buffer(i + c) *= _gain
            Next
            i += _channels
        End While
    End Sub
End Class

''' <summary>
''' Limiter: Würde eine Spitze die Obergrenze überschreiten, wird die Lautstärke sofort so weit gesenkt,
''' dass sie genau passt, und danach langsam wieder angehoben. Anders als ein Clipper verbiegt er die
''' Wellenform nicht und klingt deshalb nicht verzerrt.
''' </summary>
Public Class Limiter
    Private ReadOnly _channels As Integer
    Private ReadOnly _ceiling As Single
    Private ReadOnly _release As Single
    Private _gain As Single = 1.0F

    ''' <param name="ceilingDb">Obergrenze in dBFS (Standard -1 dB).</param>
    Public Sub New(sampleRate As Integer, channels As Integer, Optional ceilingDb As Double = -1, Optional releaseMs As Double = 250)
        _channels = Math.Max(1, channels)
        _ceiling = CSng(NoiseGate.DbToLinear(ceilingDb))
        _release = CSng(1 - Math.Exp(-1 / Math.Max(1, releaseMs * sampleRate / 1000)))
    End Sub

    ''' <summary>Aktuelle Absenkung 0…1 (1 = keine).</summary>
    Public ReadOnly Property Gain As Single
        Get
            Return _gain
        End Get
    End Property

    Public Sub Process(buffer As Single(), offset As Integer, count As Integer)
        Dim i = offset
        Dim last = offset + count - _channels
        While i <= last
            Dim peak = 0.0F
            For c = 0 To _channels - 1
                Dim v = Math.Abs(buffer(i + c))
                If v > peak Then peak = v
            Next
            _gain += (1.0F - _gain) * _release                      ' langsam zurück auf 1
            If peak * _gain > _ceiling Then _gain = _ceiling / peak  ' sofort absenken
            For c = 0 To _channels - 1
                buffer(i + c) *= _gain
            Next
            i += _channels
        End While
    End Sub
End Class
