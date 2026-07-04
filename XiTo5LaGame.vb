Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Linq

''' <summary>
''' Logic thuan tuy (khong dinh UI) cho game "Xi To 5 La An Diem" - kieu STUD nhieu vong:
''' - Dau van: Host chia 2 la cho tung nguoi choi con ket noi (chua can cuoc truoc).
''' - Sau moi dot chia (2 -> 3 -> 4 -> 5 la, tong 4 dot/vong cuoc), tung nguoi phai chon:
'''   CUOC THEM mot so diem (MIN_BET..MAX_BET, cong don vao tong da cuoc trong van) HOAC BO (Fold).
'''   Ai Fold thi MAT LUON so diem da cuoc tu truoc (khong hoan lai), va khong so bai nua.
''' - Sau dot cuoc thu 4 (du 5 la), nhung nguoi con lai (chua Fold) lat bai so tung cap kieu
'''   "an diem" nhu 3 cay: ai bai manh hon an duoc so diem bang MUC CUOC NHO HON trong 2 nguoi
'''   tu nguoi kia, hoa thi khong doi diem.
''' Class nay CHI duoc Host dung de chia bai + tinh diem (RNG). Client chi nhan du lieu Host
''' gui ve, khong tu chia bai / tu tinh ket qua.
''' </summary>
Public Class XiTo5LaGame

    Public Const STARTING_SCORE As Long = 1000
    Public Const MIN_BET As Long = 50
    Public Const MAX_BET As Long = 200
    Public Const HAND_SIZE As Integer = 5
    Public Const FIRST_DEAL_COUNT As Integer = 2   ' so la chia dot dau tien
    Public Const STREET_COUNT As Integer = 4        ' so dot cuoc: ung voi 2,3,4,5 la

    ''' <summary>Thu tu chat tu thap den cao: Chuon(Tep) &lt; Ro &lt; Co &lt; Bich.
    ''' Dung de phan dinh khi 2 bo bai hoa het muc TieBreak (rat hiem vi moi la la doc nhat).</summary>
    Public Enum CardSuit
        Chuon = 0
        Ro = 1
        Co = 2
        Bich = 3
    End Enum

    ''' <summary>1 la bai: Rank tu 2..14 (14 = At), Suit theo CardSuit.</summary>
    Public Class CardInfo
        Public ReadOnly Rank As Integer
        Public ReadOnly Suit As CardSuit

        Public Sub New(rank_ As Integer, suit_ As CardSuit)
            Rank = rank_
            Suit = suit_
        End Sub

        Public Function RankLabel() As String
            Select Case Rank
                Case 14 : Return "A"
                Case 13 : Return "K"
                Case 12 : Return "Q"
                Case 11 : Return "J"
                Case Else : Return Rank.ToString()
            End Select
        End Function

        Public Function SuitLetter() As String
            Select Case Suit
                Case CardSuit.Chuon : Return "C"
                Case CardSuit.Ro : Return "D"
                Case CardSuit.Co : Return "H"
                Case Else : Return "S"
            End Select
        End Function

        Public Function SuitSymbol() As String
            Select Case Suit
                Case CardSuit.Chuon : Return "♣"
                Case CardSuit.Ro : Return "♦"
                Case CardSuit.Co : Return "♥"
                Case Else : Return "♠"
            End Select
        End Function

        ''' <summary>La mau, dung lam ten file sprite trong Assets\Cards\, vd "AS.png", "10H.png".</summary>
        Public Function Code() As String
            Return RankLabel() & SuitLetter()
        End Function

        ''' <summary>Ma ngan gui qua mang: "rank.suit", vd "14.3" = At Bich.</summary>
        Public Function WireCode() As String
            Return Rank.ToString() & "." & CInt(Suit).ToString()
        End Function

        Public Shared Function FromWireCode(s As String) As CardInfo
            Dim parts As String() = s.Split("."c)
            Dim r As Integer = Integer.Parse(parts(0))
            Dim su As Integer = Integer.Parse(parts(1))
            Return New CardInfo(r, CType(su, CardSuit))
        End Function
    End Class

    ''' <summary>Ket qua phan loai 1 bo 5 la. Category cang cao cang manh (0..8).
    ''' TieBreak dung de so sanh khi 2 bo cung Category. DecidingSuit la chat cua la bai
    ''' co gia tri cao nhat trong tay, dung phan dinh khi TieBreak giong het (hau nhu khong xay ra
    ''' vi moi la bai la doc nhat trong bo 52 la).</summary>
    Public Class HandInfo
        Public Cards As CardInfo()
        Public Category As Integer
        Public TieBreak As Integer()
        Public DecidingSuit As CardSuit
    End Class

    ''' <summary>Ten cac loai bai, chi so tuong ung voi HandInfo.Category (0 = yeu nhat).</summary>
    Public Shared ReadOnly CategoryNames() As String = {
        "Mậu thầu", "Đôi", "Thú (2 đôi)", "Sám cô", "Sảnh", "Thùng", "Cù lũ", "Tứ quý", "Thùng phá sảnh", "Sảnh Rồng"}

    ''' <summary>Trang thai cuoc cua 1 seat trong van hien tai. Amount la TONG da cuoc don DEN THOI
    ''' DIEM HIEN TAI (cong don qua tung dot), khong phai cuoc rieng cua 1 dot.</summary>
    Public Class BetInfo
        Public Seat As Integer
        Public Amount As Long = 0
        Public Folded As Boolean = False
        Public ActedThisStreet As Boolean = False
    End Class

    ''' <summary>Ket qua 1 seat sau khi so bai xong (chi tinh cho nhung seat KHONG Fold).</summary>
    Public Class RoundOutcome
        Public Seat As Integer
        Public Amount As Long
        Public Cards As CardInfo()
        Public Hand As HandInfo
        Public Payout As Long      ' duong = duoc them, am = bi tru, tong hop tu tat ca doi thu
        Public NewScore As Long
        Public WinCount As Integer  ' so doi thu bi thang
        Public LoseCount As Integer ' so doi thu thua minh
    End Class

    Public CurrentRoundNo As Integer = 0

    ''' <summary>0 = chua bat dau van; 1..STREET_COUNT = dot cuoc hien tai, ung voi so la da chia
    ''' (dot 1 = 2 la, dot 2 = 3 la, dot 3 = 4 la, dot 4 = 5 la).</summary>
    Public CurrentStreet As Integer = 0

    Public CurrentBets As New Dictionary(Of Integer, BetInfo)
    Public DealtCards As New Dictionary(Of Integer, List(Of CardInfo))

    Private rngInstance As New Random()
    Private deck As List(Of CardInfo)
    Private deckPos As Integer = 0

    Private Function BuildShuffledDeck() As List(Of CardInfo)
        Dim newDeck As New List(Of CardInfo)
        Dim s As Integer, r As Integer
        For s = 0 To 3
            For r = 2 To 14
                newDeck.Add(New CardInfo(r, CType(s, CardSuit)))
            Next r
        Next s
        Dim i As Integer
        For i = newDeck.Count - 1 To 1 Step -1
            Dim j As Integer = rngInstance.Next(0, i + 1)
            Dim tmp As CardInfo = newDeck(i)
            newDeck(i) = newDeck(j)
            newDeck(j) = tmp
        Next i
        Return newDeck
    End Function

    ''' <summary>Chi Host goi: bat dau van moi, chia FIRST_DEAL_COUNT (2) la cho tung seat trong
    ''' activeSeats (nhung nguoi dang ket noi tai thoi diem bat dau van). Khong yeu cau cuoc truoc.</summary>
    Public Sub StartNewRound(activeSeats As List(Of Integer))
        CurrentRoundNo += 1
        CurrentStreet = 1
        CurrentBets.Clear()
        DealtCards.Clear()
        deck = BuildShuffledDeck()
        deckPos = 0

        Dim seat As Integer
        For Each seat In activeSeats
            Dim b As New BetInfo()
            b.Seat = seat
            b.Amount = 0
            b.Folded = False
            b.ActedThisStreet = False
            CurrentBets(seat) = b

            Dim cards As New List(Of CardInfo)
            Dim k As Integer
            For k = 1 To FIRST_DEAL_COUNT
                cards.Add(deck(deckPos))
                deckPos += 1
            Next k
            DealtCards(seat) = cards
        Next seat
    End Sub

    ''' <summary>Reset co "da hanh dong dot nay" cho tat ca seat con choi (chua Fold), goi truoc
    ''' khi mo 1 dot cuoc moi.</summary>
    Public Sub BeginStreetActions()
        Dim kv As KeyValuePair(Of Integer, BetInfo)
        For Each kv In CurrentBets
            If Not kv.Value.Folded Then kv.Value.ActedThisStreet = False
        Next kv
    End Sub

    ''' <summary>Danh sach seat con dang choi (chua Fold) trong van hien tai.</summary>
    Public Function ActiveSeats() As List(Of Integer)
        Dim result As New List(Of Integer)
        Dim kv As KeyValuePair(Of Integer, BetInfo)
        For Each kv In CurrentBets
            If Not kv.Value.Folded Then result.Add(kv.Key)
        Next kv
        Return result
    End Function

    ''' <summary>True neu tat ca seat con dang choi da hanh dong (cuoc them hoac bo) o dot hien tai.</summary>
    Public Function AllActedThisStreet() As Boolean
        Dim kv As KeyValuePair(Of Integer, BetInfo)
        For Each kv In CurrentBets
            If Not kv.Value.Folded AndAlso Not kv.Value.ActedThisStreet Then Return False
        Next kv
        Return True
    End Function

    ''' <summary>1 seat cuoc THEM addAmount diem (cong don vao Amount). Tra False neu du lieu
    ''' khong hop le (khong trong van, da Fold, da hanh dong dot nay, ngoai khoang MIN..MAX_BET,
    ''' hoac vuot qua diem hien co).</summary>
    Public Function Raise(seat As Integer, addAmount As Long, currentScore As Long) As Boolean
        If Not CurrentBets.ContainsKey(seat) Then Return False
        Dim b As BetInfo = CurrentBets(seat)
        If b.Folded Then Return False
        If b.ActedThisStreet Then Return False
        If addAmount < MIN_BET OrElse addAmount > MAX_BET Then Return False
        If b.Amount + addAmount > currentScore Then Return False
        b.Amount += addAmount
        b.ActedThisStreet = True
        Return True
    End Function

    ''' <summary>1 seat Bo bai (Fold): mat luon so diem da cuoc tu truoc, khong so bai nua.
    ''' Tra ve so diem bi mat (de Host tru truc tiep vao diem cua seat do), hoac 0 neu khong hop le.</summary>
    Public Function Fold(seat As Integer) As Long
        If Not CurrentBets.ContainsKey(seat) Then Return 0
        Dim b As BetInfo = CurrentBets(seat)
        If b.Folded Then Return 0
        If b.ActedThisStreet Then Return 0
        b.Folded = True
        b.ActedThisStreet = True
        Return b.Amount
    End Function

    ''' <summary>Chi Host goi sau khi 1 dot cuoc ket thuc: chia them 1 la cho tung seat con dang
    ''' choi (neu chua du HAND_SIZE), tang CurrentStreet len 1. Tra False neu khong con la nao de
    ''' chia them (da du 5 la roi).</summary>
    Public Function DealNextCard() As Boolean
        Dim dealtAny As Boolean = False
        Dim seat As Integer
        For Each seat In ActiveSeats()
            If DealtCards(seat).Count < HAND_SIZE Then
                DealtCards(seat).Add(deck(deckPos))
                deckPos += 1
                dealtAny = True
            End If
        Next seat
        If dealtAny Then CurrentStreet += 1
        Return dealtAny
    End Function

    ''' <summary>Phan loai 1 bo 5 la bai theo luat Xi To (chat thap->cao: Chuon, Ro, Co, Bich).</summary>
    Public Shared Function EvaluateHand(cards As CardInfo()) As HandInfo
        Dim ranks(4) As Integer
        Dim i As Integer
        For i = 0 To 4
            ranks(i) = cards(i).Rank
        Next i
        Dim sortedRanks As Integer() = CType(ranks.Clone(), Integer())
        Array.Sort(sortedRanks)
        Array.Reverse(sortedRanks)

        Dim countByRank As New Dictionary(Of Integer, Integer)
        For i = 0 To 4
            If countByRank.ContainsKey(ranks(i)) Then
                countByRank(ranks(i)) += 1
            Else
                countByRank(ranks(i)) = 1
            End If
        Next i

        Dim isFlush As Boolean = True
        For i = 1 To 4
            If cards(i).Suit <> cards(0).Suit Then isFlush = False
        Next i

        Dim distinctSorted As Integer() = countByRank.Keys.ToArray()
        Array.Sort(distinctSorted)
        Dim isStraight As Boolean = False
        Dim straightHigh As Integer = 0
        If distinctSorted.Length = 5 Then
            If distinctSorted(4) - distinctSorted(0) = 4 Then
                isStraight = True
                straightHigh = distinctSorted(4)
            ElseIf distinctSorted(0) = 2 AndAlso distinctSorted(1) = 3 AndAlso distinctSorted(2) = 4 AndAlso
                   distinctSorted(3) = 5 AndAlso distinctSorted(4) = 14 Then
                ' Sanh thap A-2-3-4-5: At tinh la quan thap nhat trong sanh nay.
                isStraight = True
                straightHigh = 5
            End If
        End If

        Dim groups As New List(Of KeyValuePair(Of Integer, Integer)) ' rank -> so lan xuat hien
        Dim kv As KeyValuePair(Of Integer, Integer)
        For Each kv In countByRank
            groups.Add(kv)
        Next kv
        groups.Sort(Function(x, y)
                        If x.Value <> y.Value Then Return y.Value.CompareTo(x.Value)
                        Return y.Key.CompareTo(x.Key)
                    End Function)

        Dim hand As New HandInfo()
        hand.Cards = cards

        If isStraight AndAlso isFlush AndAlso straightHigh = 14 Then
            ' Sảnh Rồng = 10-J-Q-K-A cùng chất. (straightHigh chỉ = 14 ở dây sảnh At cao thật sự;
            ' dây sảnh thấp A-2-3-4-5 luôn được gán straightHigh = 5 ở trên, nên không lẫn vào đây.)
            hand.Category = 9 : hand.TieBreak = New Integer() {straightHigh}
        ElseIf isStraight AndAlso isFlush Then
            hand.Category = 8 : hand.TieBreak = New Integer() {straightHigh}
        ElseIf groups(0).Value = 4 Then
            hand.Category = 7 : hand.TieBreak = New Integer() {groups(0).Key, groups(1).Key}
        ElseIf groups(0).Value = 3 AndAlso groups(1).Value = 2 Then
            hand.Category = 6 : hand.TieBreak = New Integer() {groups(0).Key, groups(1).Key}
        ElseIf isFlush Then
            hand.Category = 5 : hand.TieBreak = sortedRanks
        ElseIf isStraight Then
            hand.Category = 4 : hand.TieBreak = New Integer() {straightHigh}
        ElseIf groups(0).Value = 3 Then
            Dim kickers As New List(Of Integer)
            Dim g As KeyValuePair(Of Integer, Integer)
            For Each g In groups
                If g.Value = 1 Then kickers.Add(g.Key)
            Next g
            kickers.Sort() : kickers.Reverse()
            hand.Category = 3 : hand.TieBreak = New Integer() {groups(0).Key, kickers(0), kickers(1)}
        ElseIf groups(0).Value = 2 AndAlso groups(1).Value = 2 Then
            Dim pairHigh As Integer = Math.Max(groups(0).Key, groups(1).Key)
            Dim pairLow As Integer = Math.Min(groups(0).Key, groups(1).Key)
            Dim kicker As Integer = groups(2).Key
            hand.Category = 2 : hand.TieBreak = New Integer() {pairHigh, pairLow, kicker}
        ElseIf groups(0).Value = 2 Then
            Dim kickers2 As New List(Of Integer)
            Dim g2 As KeyValuePair(Of Integer, Integer)
            For Each g2 In groups
                If g2.Value = 1 Then kickers2.Add(g2.Key)
            Next g2
            kickers2.Sort() : kickers2.Reverse()
            hand.Category = 1 : hand.TieBreak = New Integer() {groups(0).Key, kickers2(0), kickers2(1), kickers2(2)}
        Else
            hand.Category = 0 : hand.TieBreak = sortedRanks
        End If

        Dim bestSuit As CardSuit = cards(0).Suit
        Dim bestRank As Integer = cards(0).Rank
        For i = 1 To 4
            If cards(i).Rank > bestRank Then
                bestRank = cards(i).Rank
                bestSuit = cards(i).Suit
            End If
        Next i
        hand.DecidingSuit = bestSuit

        Return hand
    End Function

    ''' <summary>So sanh 2 bo bai: dương neu a mạnh hơn b, âm nếu b mạnh hơn a, 0 nếu hòa tuyệt đối
    ''' (gần như không xảy ra vì mỗi lá bài là độc nhất trong bộ 52 lá).</summary>
    Public Shared Function CompareHandInfo(a As HandInfo, b As HandInfo) As Integer
        If a.Category <> b.Category Then Return a.Category.CompareTo(b.Category)
        Dim n As Integer = Math.Min(a.TieBreak.Length, b.TieBreak.Length)
        Dim i As Integer
        For i = 0 To n - 1
            If a.TieBreak(i) <> b.TieBreak(i) Then Return a.TieBreak(i).CompareTo(b.TieBreak(i))
        Next i
        Return CInt(a.DecidingSuit).CompareTo(CInt(b.DecidingSuit))
    End Function

    ''' <summary>Chi Host goi sau dot cuoc cuoi (du 5 la): so bai tung cap trong so nhung seat CON
    ''' LAI (chua Fold), kieu "an diem". Khi A thang B: ca 2 trao doi dung so diem bang MUC NHO HON
    ''' trong 2 tong cuoc (Amount) cua ho, de khong ai mat nhieu hon so diem chinh minh da cuoc cho
    ''' TUNG cap doi thu. Hoa (rat hiem) thi khong doi diem. scoresBySeat duoc cap nhat truc tiep va
    ''' tra ve danh sach ket qua tung seat con lai.</summary>
    Public Function ComputeShowdown(scoresBySeat As Dictionary(Of Integer, Long)) As List(Of RoundOutcome)
        Dim seats As List(Of Integer) = ActiveSeats()
        Dim hands As New Dictionary(Of Integer, HandInfo)
        Dim results As New Dictionary(Of Integer, RoundOutcome)

        Dim seat As Integer
        For Each seat In seats
            Dim myCards As CardInfo() = DealtCards(seat).ToArray()
            Dim h As HandInfo = EvaluateHand(myCards)
            hands(seat) = h

            Dim outcome As New RoundOutcome()
            outcome.Seat = seat
            outcome.Amount = CurrentBets(seat).Amount
            outcome.Cards = myCards
            outcome.Hand = h
            outcome.Payout = 0
            results(seat) = outcome
        Next seat

        Dim a As Integer, bIdx As Integer
        For a = 0 To seats.Count - 1
            For bIdx = a + 1 To seats.Count - 1
                Dim seatA As Integer = seats(a)
                Dim seatB As Integer = seats(bIdx)
                Dim cmp As Integer = CompareHandInfo(hands(seatA), hands(seatB))
                Dim stake As Long = Math.Min(results(seatA).Amount, results(seatB).Amount)
                If cmp > 0 Then
                    results(seatA).Payout += stake
                    results(seatA).WinCount += 1
                    results(seatB).Payout -= stake
                    results(seatB).LoseCount += 1
                ElseIf cmp < 0 Then
                    results(seatB).Payout += stake
                    results(seatB).WinCount += 1
                    results(seatA).Payout -= stake
                    results(seatA).LoseCount += 1
                End If
            Next bIdx
        Next a

        Dim finalList As New List(Of RoundOutcome)
        Dim kvR As KeyValuePair(Of Integer, RoundOutcome)
        For Each kvR In results
            Dim o As RoundOutcome = kvR.Value
            Dim oldScore As Long = 0
            If scoresBySeat.ContainsKey(o.Seat) Then oldScore = scoresBySeat(o.Seat)
            Dim newScore As Long = oldScore + o.Payout
            scoresBySeat(o.Seat) = newScore
            o.NewScore = newScore
            finalList.Add(o)
        Next kvR

        Return finalList
    End Function

End Class
