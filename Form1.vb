Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Drawing.Drawing2D
Imports System.Windows.Forms
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text
Imports System.IO
Imports System.Linq
Imports Microsoft.VisualBasic

''' <summary>
''' Game "Xì Tố 5 Lá Ăn Điểm": tối đa 4 người chơi (1 Host + 3 Client). Đầu ván, Host chia 2 lá
''' cho từng người đang kết nối (không cần cược trước). Sau mỗi đợt chia (2 -> 3 -> 4 -> 5 lá,
''' tổng 4 đợt cược), từng người chọn CƯỢC THÊM một số điểm hoặc BỎ (Fold) - ai bỏ thì mất luôn
''' số điểm đã cược và không so bài nữa. Sau đợt cược thứ 4 (đủ 5 lá), những người còn lại lật bài
''' so từng cặp kiểu "ăn điểm" giống 3 cây: ai bài mạnh hơn ăn của người kia đúng mức cược nhỏ hơn
''' trong 2 người, hoà thì không đổi điểm. Kiến trúc mạng tái sử dụng nguyên NetworkHub.vb
''' (Host) / NetworkPeer.vb (Client) từ dự án Vòng Quay Rồng.
''' </summary>
Public Class Form1
    Inherits Form

    Private Const DEFAULT_PORT As Integer = 9060
    Private Const BETTING_SECONDS As Integer = 20
    Private Const JACKPOT_RAKE_PERCENT As Double = 0.05   ' trích 5% mỗi lượt Cược thêm vào quỹ Jackpot
    Private Const JACKPOT_PAYOUT_PERCENT As Double = 0.5  ' ai ăn Sảnh Rồng thì ăn 50% quỹ hiện có
    Private Const BOARD_W As Integer = 560
    Private Const BOARD_H As Integer = 230

    Private Enum RoundState
        Idle
        Betting
        Revealing
        ShowingResult
    End Enum

    ' ------------------- Mạng -------------------
    Private hub As NetworkHub
    Private jackpotPool As Long = 0
    Private peer As NetworkPeer
    Private isHost As Boolean = False
    Private localSeat As Integer = -1
    Private playerNames(3) As String
    Private playerConnected(3) As Boolean

    ' ------------------- Game -------------------
    Private game As New XiTo5LaGame()
    Private scoresBySeat As New Dictionary(Of Integer, Long)
    Private state As RoundState = RoundState.Idle
    Private secondsLeft As Integer = 0
    Private countdownTimer As Timer

    Private hasActedThisStreet As Boolean = False

    ' trạng thái đang chơi (cập nhật theo từng gói XT5_MYCARDS / XT5_CARDCOUNT / XT5_LOCK)
    Private perSeatBetAmount(3) As Long          ' -1 = chưa vào ván này ; >=0 = tổng cược cộng dồn
    Private perSeatFolded(3) As Boolean          ' đã Bỏ bài chưa
    Private perSeatActedThisStreet(3) As Boolean ' đã hành động (cược thêm/bỏ) ở đợt hiện tại chưa
    Private perSeatCardCount(3) As Integer       ' số lá đã có trong tay (0..5)
    Private perSeatMyCardList(3) As List(Of XiTo5LaGame.CardInfo) ' chỉ đầy đủ với CHÍNH MÌNH; Nothing nếu không phải mình

    ' kết quả sau khi so bài (chỉ có với những seat KHÔNG Fold)
    Private perSeatCards As XiTo5LaGame.CardInfo()() = New XiTo5LaGame.CardInfo(3)() {}  ' Nothing = đã Fold hoặc chưa có kết quả
    Private perSeatCategory(3) As Integer        ' -1 = chưa có kết quả
    Private perSeatPayout(3) As Long
    Private perSeatJackpotBonus(3) As Long
    Private perSeatWin(3) As Integer
    Private perSeatLose(3) As Integer

    ' cache ván gần nhất đã broadcast (đồng bộ cho người vào phòng giữa ván)
    Private lastRevealEntriesRaw As String = Nothing

    ' hiệu ứng lật bài: lật lá của MÌNH trước (từng lá 1), rồi lật hết bài đối thủ
    Private revealTimer As Timer
    Private revealStep As Integer = 0   ' 0..5 : 1..5 = đã lộ N lá đầu của MÌNH ; 6 = lộ hết đối thủ + xong

    ' hiệu ứng lấp lánh khi có bài mạnh (Tứ quý trở lên): viền phát sáng nhấp nháy + tia sao quanh bộ bài
    Private Const SPARKLE_MIN_CATEGORY As Integer = 7   ' 7 = Tứ quý, 8 = Thùng phá sảnh, 9 = Sảnh Rồng
    Private sparkleTimer As Timer
    Private sparklePhase As Double = 0.0

    ' ------------------- Nhật ký ván đấu -------------------
    Private Const HISTORY_MAX As Integer = 30
    Private roundLog As New List(Of String)  ' phần tử 0 = ván gần nhất
    Private lstHistory As ListBox

    ' ------------------- Ảnh sprite lá bài (Assets\Cards\*.png), có thể null nếu thiếu file -------------------
    Private cardImages As New Dictionary(Of String, Image)
    Private cardBackImage As Image

    ' ------------------- Sprite hiệu ứng lấp lánh / rồng / nổ Jackpot (Assets\Effects\*.png) -------------------
    Private spriteSparkleSheet As Image      ' 6 khung, dùng cho Tứ quý & Thùng phá sảnh
    Private spriteSanhRongSheet As Image     ' 8 khung, dùng riêng cho Sảnh Rồng
    Private spriteJackpotSheet As Image      ' 8 khung, phát 1 lần khi ăn Jackpot
    Private Const SPARKLE_FRAME_COUNT As Integer = 6
    Private Const SANHRONG_FRAME_COUNT As Integer = 8
    Private Const JACKPOT_FRAME_COUNT As Integer = 8
    Private perSeatJackpotBurstStep(3) As Integer  ' -1 = không phát ; 0..JACKPOT_FRAME_COUNT = đang phát/đã xong

    ' ------------------- UI: Connect panel -------------------
    Private pnlConnect As Panel
    Private txtName As TextBox
    Private txtIP As TextBox
    Private txtPort As TextBox
    Private btnHost As Button
    Private btnJoin As Button
    Private lblConnectStatus As Label

    ' ------------------- UI: Game panel -------------------
    Private pnlGame As Panel
    Private pnlRoundBanner As Panel
    Private pnlMyHand As Panel
    Private lblRoundInfo As Label
    Private lblCountdown As Label
    Private nudBet As NumericUpDown
    Private btnLockBet As Button
    Private btnFold As Button
    Private btnHostAction As Button
    Private pnlPlayers(3) As Panel
    Private pnlCardsRow(3) As Panel
    Private lblCardTitle(3) As Label
    Private lblCardStatus(3) As Label
    Private lblCardResult(3) As Label

    ' ------------------- UI: Chat panel -------------------
    Private pnlChat As Panel
    Private lstChat As ListBox
    Private txtChatInput As TextBox
    Private btnSend As Button

    Public Sub New()
        Me.Text = "Xì Tố 5 Lá Ăn Điểm"
        Me.ClientSize = New Size(940, 700)
        Me.StartPosition = FormStartPosition.CenterScreen
        Dim i As Integer
        For i = 0 To 3
            playerNames(i) = "Người chơi " & (i + 1).ToString()
            playerConnected(i) = False
            scoresBySeat(i) = XiTo5LaGame.STARTING_SCORE
            perSeatBetAmount(i) = -1
            perSeatFolded(i) = False
            perSeatActedThisStreet(i) = False
            perSeatCardCount(i) = 0
            perSeatMyCardList(i) = Nothing
            perSeatCards(i) = Nothing
            perSeatCategory(i) = -1
            perSeatJackpotBonus(i) = 0
            perSeatJackpotBurstStep(i) = -1
        Next i
        BuildConnectPanel()
        LoadCardSprites()
        LoadEffectSprites()
    End Sub

    ''' <summary>Nạp sprite 52 lá bài + mặt sau từ thư mục "Assets\Cards" (đặt cạnh file .exe).
    ''' Tên file theo mẫu: {Rank}{Suit}.png, ví dụ "AS.png" (Át Bích), "10H.png" (10 Cơ),
    ''' "2C.png" (2 Chuồn/Tép). Mặt sau: "back.png". Thiếu file nào thì lá đó tự vẽ fallback
    ''' bằng GDI+ (không bị crash).</summary>
    Private Sub LoadCardSprites()
        Dim dir As String = Path.Combine(Path.Combine(Application.StartupPath, "Assets"), "Cards")
        Dim suits() As XiTo5LaGame.CardSuit = {XiTo5LaGame.CardSuit.Chuon, XiTo5LaGame.CardSuit.Ro, XiTo5LaGame.CardSuit.Co, XiTo5LaGame.CardSuit.Bich}
        Dim su As XiTo5LaGame.CardSuit
        For Each su In suits
            Dim r As Integer
            For r = 2 To 14
                Dim c As New XiTo5LaGame.CardInfo(r, su)
                Dim code As String = c.Code()
                Try
                    Dim fp As String = Path.Combine(dir, code & ".png")
                    If File.Exists(fp) Then
                        cardImages(code) = Image.FromFile(fp)
                    End If
                Catch
                    ' anh loi/hong -> fallback ve tay, khong crash
                End Try
            Next r
        Next su
        Try
            Dim backPath As String = Path.Combine(dir, "back.png")
            If File.Exists(backPath) Then
                cardBackImage = Image.FromFile(backPath)
            End If
        Catch
            cardBackImage = Nothing
        End Try
    End Sub

    ''' <summary>Nạp 3 sprite sheet hiệu ứng từ "Assets\Effects" (đặt cạnh file .exe), mỗi sheet là
    ''' 1 dải khung hình xếp ngang đều nhau, nền trong suốt:
    ''' - sparkle.png (6 khung): hiệu ứng lấp lánh dùng cho Tứ quý &amp; Thùng phá sảnh.
    ''' - sanhrong.png (8 khung): rồng vàng lượn quanh lá bài, dùng riêng cho Sảnh Rồng.
    ''' - jackpot_burst.png (8 khung): hiệu ứng nổ xu vàng, phát 1 lần khi có người ăn Jackpot.
    ''' Thiếu file nào thì hiệu ứng đó tự rơi về kiểu vẽ tay GDI+ cũ (không crash).</summary>
    Private Sub LoadEffectSprites()
        Dim dir As String = Path.Combine(Path.Combine(Application.StartupPath, "Assets"), "Effects")
        Try
            Dim fp As String = Path.Combine(dir, "sparkle.png")
            If File.Exists(fp) Then spriteSparkleSheet = Image.FromFile(fp)
        Catch
            spriteSparkleSheet = Nothing
        End Try
        Try
            Dim fp As String = Path.Combine(dir, "sanhrong.png")
            If File.Exists(fp) Then spriteSanhRongSheet = Image.FromFile(fp)
        Catch
            spriteSanhRongSheet = Nothing
        End Try
        Try
            Dim fp As String = Path.Combine(dir, "jackpot_burst.png")
            If File.Exists(fp) Then spriteJackpotSheet = Image.FromFile(fp)
        Catch
            spriteJackpotSheet = Nothing
        End Try
    End Sub

    ' ============================================================
    '  MÀU THEO SEAT
    ' ============================================================
    Private Function PlayerColor(seat As Integer) As Color
        Select Case seat
            Case 0 : Return Color.FromArgb(200, 40, 40)
            Case 1 : Return Color.FromArgb(30, 110, 200)
            Case 2 : Return Color.FromArgb(30, 150, 70)
            Case Else : Return Color.FromArgb(160, 90, 190)
        End Select
    End Function

    ' ============================================================
    '  CONNECT PANEL (chọn Host hoặc Join)
    ' ============================================================
    Private Sub BuildConnectPanel()
        pnlConnect = New Panel()
        pnlConnect.Dock = DockStyle.Fill
        pnlConnect.BackColor = Color.FromArgb(245, 245, 240)

        Dim lblTitle As New Label()
        lblTitle.Text = "XÌ TỐ 5 LÁ ĂN ĐIỂM"
        lblTitle.Font = New Font("Segoe UI", 18.0!, FontStyle.Bold)
        lblTitle.AutoSize = True
        lblTitle.Location = New Point(40, 30)
        pnlConnect.Controls.Add(lblTitle)

        Dim lblName As New Label() : lblName.Text = "Tên của bạn:" : lblName.AutoSize = True
        lblName.Location = New Point(40, 100)
        pnlConnect.Controls.Add(lblName)
        txtName = New TextBox() : txtName.Location = New Point(40, 122) : txtName.Size = New Size(220, 24)
        txtName.Text = "Người chơi"
        pnlConnect.Controls.Add(txtName)

        Dim lblPort As New Label() : lblPort.Text = "Cổng (Port):" : lblPort.AutoSize = True
        lblPort.Location = New Point(40, 160)
        pnlConnect.Controls.Add(lblPort)
        txtPort = New TextBox() : txtPort.Location = New Point(40, 182) : txtPort.Size = New Size(220, 24)
        txtPort.Text = DEFAULT_PORT.ToString()
        pnlConnect.Controls.Add(txtPort)

        btnHost = New Button() : btnHost.Text = "Tạo phòng (Host)"
        btnHost.Location = New Point(40, 220) : btnHost.Size = New Size(220, 34)
        AddHandler btnHost.Click, AddressOf BtnHost_Click
        pnlConnect.Controls.Add(btnHost)

        Dim lblIP As New Label() : lblIP.Text = "IP của Host:" : lblIP.AutoSize = True
        lblIP.Location = New Point(40, 280)
        pnlConnect.Controls.Add(lblIP)
        txtIP = New TextBox() : txtIP.Location = New Point(40, 302) : txtIP.Size = New Size(220, 24)
        txtIP.Text = "127.0.0.1"
        pnlConnect.Controls.Add(txtIP)

        btnJoin = New Button() : btnJoin.Text = "Vào phòng (Join)"
        btnJoin.Location = New Point(40, 336) : btnJoin.Size = New Size(220, 34)
        AddHandler btnJoin.Click, AddressOf BtnJoin_Click
        pnlConnect.Controls.Add(btnJoin)

        lblConnectStatus = New Label()
        lblConnectStatus.Location = New Point(40, 390) : lblConnectStatus.Size = New Size(400, 60)
        lblConnectStatus.ForeColor = Color.DimGray
        pnlConnect.Controls.Add(lblConnectStatus)

        Me.Controls.Add(pnlConnect)
    End Sub

    Private Sub BtnHost_Click(sender As Object, e As EventArgs)
        Dim port As Integer
        If Not Integer.TryParse(txtPort.Text.Trim(), port) Then
            MessageBox.Show("Port không hợp lệ.") : Return
        End If
        isHost = True
        localSeat = 0
        playerNames(0) = SafeName(txtName.Text)
        playerConnected(0) = True

        hub = New NetworkHub(Me)
        AddHandler hub.ClientConnected, AddressOf Hub_ClientConnected
        AddHandler hub.ClientDisconnected, AddressOf Hub_ClientDisconnected
        AddHandler hub.LineReceivedFromClient, AddressOf Hub_LineReceived
        hub.StartListening(port)

        lblConnectStatus.Text = "Đang chờ người chơi kết nối trên cổng " & port.ToString() & " ..."
        ShowGamePanel()
    End Sub

    Private Sub BtnJoin_Click(sender As Object, e As EventArgs)
        Dim port As Integer
        If Not Integer.TryParse(txtPort.Text.Trim(), port) Then
            MessageBox.Show("Port không hợp lệ.") : Return
        End If
        isHost = False
        playerNames(0) = SafeName(txtName.Text)

        peer = New NetworkPeer(Me)
        AddHandler peer.Connected, AddressOf Peer_Connected
        AddHandler peer.Disconnected, AddressOf Peer_Disconnected
        AddHandler peer.LineReceived, AddressOf Peer_LineReceived
        peer.ConnectToHost(txtIP.Text.Trim(), port)

        lblConnectStatus.Text = "Đang kết nối đến " & txtIP.Text.Trim() & ":" & port.ToString() & " ..."
    End Sub

    Private Function SafeName(raw As String) As String
        Dim s As String = raw.Trim()
        If s = "" Then Return "Người chơi"
        If s.Length > 16 Then s = s.Substring(0, 16)
        Return s
    End Function

    ' ============================================================
    '  SỰ KIỆN MẠNG - PHÍA CLIENT (NetworkPeer)
    ' ============================================================
    Private Sub Peer_Connected()
        peer.SendLine("XT5_HELLO:" & playerNames(0))
    End Sub

    Private Sub Peer_Disconnected()
        AppendChat("[Hệ thống] Mất kết nối tới Host.")
    End Sub

    Private Sub Peer_LineReceived(line As String)
        HandleProtocolLine(line, -1)
    End Sub

    ' ============================================================
    '  SỰ KIỆN MẠNG - PHÍA HOST (NetworkHub)
    ' ============================================================
    Private Sub Hub_ClientConnected(seatIndex As Integer)
        playerConnected(seatIndex) = True
        hub.SendToClient(seatIndex, "XT5_WELCOME:" & seatIndex.ToString())
        BroadcastNames()
        BroadcastScores()
        BroadcastConnected()
        hub.SendToClient(seatIndex, "XT5_JACKPOT:" & jackpotPool.ToString())
        If roundLog.Count > 0 Then
            hub.SendToClient(seatIndex, "XT5_HISTORY:" & String.Join("~~", roundLog))
        End If
        SyncStateToLateJoiner(seatIndex)
        RefreshPlayerCards()
        AppendChat("[Hệ thống] Player " & (seatIndex + 1).ToString() & " đã vào phòng.")
    End Sub

    ''' <summary>Nếu có người vào phòng khi ván đấu đã bắt đầu, gửi riêng cho seat đó đủ dữ liệu
    ''' hiện tại (đếm số lá từng người, ai đã cược/bỏ bao nhiêu, kết quả gần nhất nếu đã lộ) để
    ''' màn hình không bị kẹt. Người vào giữa ván KHÔNG được chia bài, phải chờ ván sau.</summary>
    Private Sub SyncStateToLateJoiner(seatIndex As Integer)
        If state = RoundState.Idle Then Return

        hub.SendToClient(seatIndex, "XT5_ROUND:" & game.CurrentRoundNo.ToString())

        Dim counts(3) As Integer
        Dim s As Integer
        For s = 0 To 3
            counts(s) = perSeatCardCount(s)
        Next s
        hub.SendToClient(seatIndex, "XT5_CARDCOUNT:" & counts(0).ToString() & "|" & counts(1).ToString() & "|" & counts(2).ToString() & "|" & counts(3).ToString())

        For s = 0 To 3
            If perSeatBetAmount(s) >= 0 Then
                hub.SendToClient(seatIndex, "XT5_LOCK:" & s.ToString() & "|" & perSeatBetAmount(s).ToString() & "|" & CInt(IIf(perSeatFolded(s), 1, 0)).ToString())
            End If
        Next s

        If state = RoundState.Betting Then
            hub.SendToClient(seatIndex, "XT5_STREET:" & game.CurrentStreet.ToString() & "|" & Math.Max(0, secondsLeft).ToString())
        ElseIf (state = RoundState.Revealing OrElse state = RoundState.ShowingResult) AndAlso lastRevealEntriesRaw IsNot Nothing Then
            hub.SendToClient(seatIndex, "XT5_REVEAL:" & lastRevealEntriesRaw)
        End If
    End Sub

    Private Sub Hub_ClientDisconnected(seatIndex As Integer)
        playerConnected(seatIndex) = False
        playerNames(seatIndex) = "Người chơi " & (seatIndex + 1).ToString()
        BroadcastNames()
        BroadcastConnected()
        RefreshPlayerCards()
        AppendChat("[Hệ thống] Player " & (seatIndex + 1).ToString() & " đã rời phòng.")
    End Sub

    Private Sub Hub_LineReceived(seatIndex As Integer, line As String)
        HandleProtocolLine(line, seatIndex)
    End Sub

    ' ============================================================
    '  XỬ LÝ GIAO THỨC CHUNG
    '  fromSeat = -1 khi đây là Client đang nhận tin từ Host (không cần biết seat người gửi)
    '  fromSeat = 0..3 khi đây là Host đang nhận tin từ 1 Client cụ thể (seat đó)
    ' ============================================================
    Private Sub HandleProtocolLine(line As String, fromSeat As Integer)
        If line Is Nothing OrElse line = "" Then Return
        Dim idx As Integer = line.IndexOf(":"c)
        Dim msgType As String = If(idx >= 0, line.Substring(0, idx), line)
        Dim payload As String = If(idx >= 0, line.Substring(idx + 1), "")

        Select Case msgType
            Case "CHAT"
                Dim p2 As Integer = payload.IndexOf(":"c)
                If p2 >= 0 Then
                    AppendChat(payload.Substring(0, p2) & ": " & payload.Substring(p2 + 1))
                End If
                If isHost Then hub.BroadcastExcept("CHAT:" & payload, fromSeat)

            Case "XT5_WELCOME"
                localSeat = Integer.Parse(payload, CultureInfo.InvariantCulture)
                ShowGamePanel()
                lblConnectStatus.Text = "Đã vào phòng, bạn là Player " & (localSeat + 1).ToString()

            Case "XT5_HELLO"
                If fromSeat >= 0 Then
                    playerNames(fromSeat) = SafeName(payload)
                    BroadcastNames()
                    RefreshPlayerCards()
                End If

            Case "XT5_NAMES"
                Dim parts As String() = payload.Split("|"c)
                Dim i As Integer
                For i = 0 To Math.Min(3, parts.Length - 1)
                    If parts(i) <> "" Then playerNames(i) = parts(i)
                Next i
                RefreshPlayerCards()

            Case "XT5_SCORES"
                Dim sp As String() = payload.Split("|"c)
                Dim i2 As Integer
                For i2 = 0 To Math.Min(3, sp.Length - 1)
                    Dim v As Long
                    If Long.TryParse(sp(i2), NumberStyles.Integer, CultureInfo.InvariantCulture, v) Then
                        scoresBySeat(i2) = v
                    End If
                Next i2
                RefreshPlayerCards()

            Case "XT5_CONN"
                Dim cp As String() = payload.Split("|"c)
                Dim i3 As Integer
                For i3 = 0 To Math.Min(3, cp.Length - 1)
                    playerConnected(i3) = (cp(i3).Trim() = "1")
                Next i3
                RefreshPlayerCards()

            Case "XT5_JACKPOT"
                Dim jp As Long
                If Long.TryParse(payload, NumberStyles.Integer, CultureInfo.InvariantCulture, jp) Then
                    jackpotPool = jp
                    RefreshJackpotLabel()
                End If

            Case "XT5_ROUND"
                Dim roundNo As Integer = Integer.Parse(payload, CultureInfo.InvariantCulture)
                BeginNewRoundLocal(roundNo)

            Case "XT5_MYCARDS"
                ApplyMyCards(payload)

            Case "XT5_CARDCOUNT"
                Dim ccp As String() = payload.Split("|"c)
                Dim i4 As Integer
                For i4 = 0 To Math.Min(3, ccp.Length - 1)
                    Dim v2 As Integer
                    If Integer.TryParse(ccp(i4), NumberStyles.Integer, CultureInfo.InvariantCulture, v2) Then
                        perSeatCardCount(i4) = v2
                    End If
                Next i4
                RefreshPlayerCards()
                If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()

            Case "XT5_STREET"
                Dim stp As String() = payload.Split("|"c)
                Dim streetNo As Integer = Integer.Parse(stp(0), CultureInfo.InvariantCulture)
                Dim secs As Integer = Integer.Parse(stp(1), CultureInfo.InvariantCulture)
                BeginStreetLocal(streetNo, secs)

            Case "XT5_LOCK"
                Dim lp As String() = payload.Split("|"c)
                Dim seat As Integer = Integer.Parse(lp(0), CultureInfo.InvariantCulture)
                Dim amount As Long = Long.Parse(lp(1), CultureInfo.InvariantCulture)
                Dim folded As Boolean = (lp.Length > 2 AndAlso lp(2) = "1")
                perSeatBetAmount(seat) = amount
                perSeatFolded(seat) = folded
                perSeatActedThisStreet(seat) = True
                RefreshPlayerCards()

            Case "XT5_RAISE"
                If fromSeat >= 0 AndAlso isHost Then
                    Dim amount2 As Long = Long.Parse(payload, CultureInfo.InvariantCulture)
                    ProcessRaiseFromSeat(fromSeat, amount2)
                End If

            Case "XT5_FOLD"
                If fromSeat >= 0 AndAlso isHost Then
                    ProcessFoldFromSeat(fromSeat)
                End If

            Case "XT5_ACT_NACK"
                UnlockLocalActionUI()

            Case "XT5_REVEAL"
                lastRevealEntriesRaw = payload
                StartRevealAnimation(payload)

            Case "XT5_HISTORY"
                roundLog.Clear()
                If payload.Trim() <> "" Then
                    Dim hp As String() = payload.Split(New String() {"~~"}, StringSplitOptions.None)
                    Dim hv As String
                    For Each hv In hp
                        roundLog.Add(hv)
                    Next hv
                End If
                RefreshHistoryList()
        End Select
    End Sub

    ' ============================================================
    '  GỬI DỮ LIỆU (Host broadcast trạng thái dùng cho cả Host lẫn Client)
    ' ============================================================
    Private Sub BroadcastNames()
        If Not isHost Then Return
        Dim s As String = playerNames(0) & "|" & playerNames(1) & "|" & playerNames(2) & "|" & playerNames(3)
        hub.Broadcast("XT5_NAMES:" & s)
    End Sub

    Private Sub BroadcastScores()
        If Not isHost Then Return
        Dim s As String = scoresBySeat(0).ToString() & "|" & scoresBySeat(1).ToString() & "|" &
                           scoresBySeat(2).ToString() & "|" & scoresBySeat(3).ToString()
        hub.Broadcast("XT5_SCORES:" & s)
    End Sub

    Private Sub BroadcastConnected()
        If Not isHost Then Return
        Dim s As String = CInt(IIf(playerConnected(0), 1, 0)).ToString() & "|" &
                           CInt(IIf(playerConnected(1), 1, 0)).ToString() & "|" &
                           CInt(IIf(playerConnected(2), 1, 0)).ToString() & "|" &
                           CInt(IIf(playerConnected(3), 1, 0)).ToString()
        hub.Broadcast("XT5_CONN:" & s)
    End Sub

    ' ============================================================
    '  VÒNG CƯỢC NHIỀU ĐỢT (Host điều khiển state machine)
    ' ============================================================
    Private Sub BtnHostAction_Click(sender As Object, e As EventArgs)
        If Not isHost Then Return
        Select Case state
            Case RoundState.Idle, RoundState.ShowingResult
                Dim activeSeats As New List(Of Integer)
                Dim s As Integer
                For s = 0 To 3
                    If playerConnected(s) Then activeSeats.Add(s)
                Next s
                If activeSeats.Count = 0 Then Return

                game.StartNewRound(activeSeats)
                lastRevealEntriesRaw = Nothing
                hub.Broadcast("XT5_ROUND:" & game.CurrentRoundNo.ToString())
                BeginNewRoundLocal(game.CurrentRoundNo)
                BroadcastDealtCards()
                hub.Broadcast("XT5_STREET:" & game.CurrentStreet.ToString() & "|" & BETTING_SECONDS.ToString())
                BeginStreetLocal(game.CurrentStreet, BETTING_SECONDS)

            Case Else
                ' đang trong 1 đợt cược hoặc đang lật bài, không làm gì (Host chỉ bấm khi Idle/ShowingResult)
        End Select
    End Sub

    ''' <summary>Reset toàn bộ dữ liệu hiển thị cho ván mới (gọi trên cả Host lẫn Client khi nhận XT5_ROUND).</summary>
    Private Sub BeginNewRoundLocal(roundNo As Integer)
        state = RoundState.Idle
        hasActedThisStreet = False
        StopSparkle()
        Dim i As Integer
        For i = 0 To 3
            perSeatBetAmount(i) = -1
            perSeatFolded(i) = False
            perSeatActedThisStreet(i) = False
            perSeatCardCount(i) = 0
            perSeatMyCardList(i) = Nothing
            perSeatCards(i) = Nothing
            perSeatCategory(i) = -1
            perSeatPayout(i) = 0
            perSeatWin(i) = 0
            perSeatLose(i) = 0
            perSeatJackpotBonus(i) = 0
            perSeatJackpotBurstStep(i) = -1
            If lblCardResult(i) IsNot Nothing Then lblCardResult(i).Text = ""
            If pnlCardsRow(i) IsNot Nothing Then pnlCardsRow(i).Invalidate()
        Next i
        lblRoundInfo.Text = "Ván " & roundNo.ToString() & " - Host đang chia bài..."
        If pnlRoundBanner IsNot Nothing Then pnlRoundBanner.Invalidate()
        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        RefreshPlayerCards()
    End Sub

    ''' <summary>Chỉ Host gọi: gửi riêng cho từng seat bài hiện có của họ (XT5_MYCARDS, KHÔNG lộ
    ''' cho người khác), rồi broadcast công khai số lá của tất cả (XT5_CARDCOUNT) để mọi người
    ''' vẽ được ô bài úp của đối thủ.</summary>
    Private Sub BroadcastDealtCards()
        Dim counts(3) As Integer
        Dim s As Integer
        For s = 0 To 3
            If game.DealtCards.ContainsKey(s) Then
                Dim cards As List(Of XiTo5LaGame.CardInfo) = game.DealtCards(s)
                counts(s) = cards.Count
                Dim wire As String = String.Join("_", cards.Select(Function(c) c.WireCode()))
                If s = 0 AndAlso isHost Then
                    ApplyMyCards(wire)
                Else
                    hub.SendToClient(s, "XT5_MYCARDS:" & wire)
                End If
            Else
                counts(s) = 0
            End If
        Next s
        hub.Broadcast("XT5_CARDCOUNT:" & counts(0).ToString() & "|" & counts(1).ToString() & "|" & counts(2).ToString() & "|" & counts(3).ToString())
    End Sub

    ''' <summary>Nhận XT5_MYCARDS (bài của CHÍNH MÌNH): "rank.suit_rank.suit_...".</summary>
    Private Sub ApplyMyCards(wirePayload As String)
        Dim mySeat As Integer = If(isHost, 0, localSeat)
        If mySeat < 0 Then Return
        Dim codes As String() = wirePayload.Split("_"c)
        Dim list As New List(Of XiTo5LaGame.CardInfo)
        Dim code As String
        For Each code In codes
            If code.Trim() <> "" Then list.Add(XiTo5LaGame.CardInfo.FromWireCode(code))
        Next code
        perSeatMyCardList(mySeat) = list
        perSeatCardCount(mySeat) = list.Count
        RefreshPlayerCards()
        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        If pnlCardsRow(mySeat) IsNot Nothing Then pnlCardsRow(mySeat).Invalidate()
    End Sub

    ''' <summary>Bắt đầu 1 đợt cược (streetNo 1..4). Gọi trên cả Host lẫn Client.</summary>
    Private Sub BeginStreetLocal(streetNo As Integer, secs As Integer)
        state = RoundState.Betting
        hasActedThisStreet = False
        Dim s As Integer
        For s = 0 To 3
            If Not perSeatFolded(s) Then perSeatActedThisStreet(s) = False
        Next s
        secondsLeft = secs

        Dim cardsThisStreet As Integer = XiTo5LaGame.FIRST_DEAL_COUNT + (streetNo - 1)
        lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - Đợt cược " & streetNo.ToString() &
            "/" & XiTo5LaGame.STREET_COUNT.ToString() & " (" & cardsThisStreet.ToString() & " lá) - cược thêm hoặc bỏ bài!"
        lblCountdown.Text = "Còn: " & secondsLeft.ToString() & "s"

        Dim mySeat As Integer = If(isHost, 0, localSeat)
        Dim canAct As Boolean = (mySeat >= 0 AndAlso perSeatCardCount(mySeat) > 0 AndAlso Not perSeatFolded(mySeat))
        btnLockBet.Enabled = canAct
        btnFold.Enabled = canAct
        nudBet.Enabled = canAct

        If isHost Then
            btnHostAction.Text = "(đang chờ người chơi cược/bỏ...)"
            btnHostAction.Enabled = False
        Else
            btnHostAction.Visible = False
        End If

        If countdownTimer Is Nothing Then
            countdownTimer = New Timer()
            countdownTimer.Interval = 1000
            AddHandler countdownTimer.Tick, AddressOf CountdownTimer_Tick
        End If
        countdownTimer.Start()

        If pnlRoundBanner IsNot Nothing Then pnlRoundBanner.Invalidate()
        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        RefreshPlayerCards()
    End Sub

    Private Sub CountdownTimer_Tick(sender As Object, e As EventArgs)
        secondsLeft -= 1
        lblCountdown.Text = "Còn: " & Math.Max(0, secondsLeft).ToString() & "s"
        If secondsLeft <= 0 Then
            countdownTimer.Stop()
            If isHost Then AutoFoldRemainingAndAdvance()
        End If
    End Sub

    ''' <summary>Chỉ Host gọi khi hết giờ 1 đợt cược: những ai chưa hành động bị tự động Bỏ bài,
    ''' sau đó chuyển sang đợt kế tiếp (hoặc so bài nếu đã đủ 5 lá).</summary>
    Private Sub AutoFoldRemainingAndAdvance()
        If Not isHost Then Return
        Dim s As Integer
        For Each s In game.ActiveSeats()
            If Not game.CurrentBets(s).ActedThisStreet Then
                DoFoldSeat(s)
            End If
        Next s
        AdvanceStreetOrShowdown()
    End Sub

    ''' <summary>Chỉ Host gọi: xử lý 1 lượt Bỏ bài (dùng chung cho hành động chủ động của người
    ''' chơi lẫn tự động bỏ khi hết giờ).</summary>
    Private Sub DoFoldSeat(seat As Integer)
        Dim lostAmount As Long = game.Fold(seat)
        perSeatFolded(seat) = True
        perSeatBetAmount(seat) = lostAmount
        perSeatActedThisStreet(seat) = True
        Dim cur As Long = If(scoresBySeat.ContainsKey(seat), scoresBySeat(seat), 0L)
        scoresBySeat(seat) = cur - lostAmount
        hub.Broadcast("XT5_LOCK:" & seat.ToString() & "|" & lostAmount.ToString() & "|1")
        BroadcastScores()
        RefreshPlayerCards()
    End Sub

    ''' <summary>Chỉ Host gọi sau khi tất cả seat còn chơi đã hành động ở đợt hiện tại: chia thêm
    ''' 1 lá nếu chưa đủ 5 (mở đợt cược tiếp theo), hoặc so bài nếu đã đủ 5 lá / chỉ còn <= 1 người.</summary>
    Private Sub AdvanceStreetOrShowdown()
        If Not isHost Then Return
        If countdownTimer IsNot Nothing Then countdownTimer.Stop()

        Dim activeCount As Integer = game.ActiveSeats().Count
        If activeCount <= 1 OrElse game.CurrentStreet >= XiTo5LaGame.STREET_COUNT Then
            DoShowdown()
        Else
            game.DealNextCard()
            game.BeginStreetActions()
            BroadcastDealtCards()
            hub.Broadcast("XT5_STREET:" & game.CurrentStreet.ToString() & "|" & BETTING_SECONDS.ToString())
            BeginStreetLocal(game.CurrentStreet, BETTING_SECONDS)
        End If
    End Sub

    ''' <summary>Host xử lý 1 lượt CƯỢC THÊM gửi lên từ Client (hoặc từ chính Host).
    ''' Nếu không hợp lệ, báo NACK về đúng seat đó để UI của họ được mở khoá lại.</summary>
    Private Sub ProcessRaiseFromSeat(seat As Integer, addAmount As Long)
        If state <> RoundState.Betting Then
            RejectAction(seat)
            Return
        End If
        Dim currentScore As Long = 0L
        If scoresBySeat.ContainsKey(seat) Then currentScore = scoresBySeat(seat)
        If Not game.Raise(seat, addAmount, currentScore) Then
            RejectAction(seat)
            Return
        End If
        Dim b As XiTo5LaGame.BetInfo = game.CurrentBets(seat)
        perSeatBetAmount(seat) = b.Amount
        perSeatActedThisStreet(seat) = True

        ' Trích % nhỏ vào quỹ Jackpot, trừ trực tiếp trên điểm của người vừa cược (không tính vào
        ' mức cược Amount dùng để so bài, vì đây là "thuế" nộp quỹ, mất luôn ngay lúc cược).
        Dim rake As Long = CLng(Math.Floor(addAmount * JACKPOT_RAKE_PERCENT))
        If rake > 0 Then
            scoresBySeat(seat) = Math.Max(0L, currentScore - rake)
            jackpotPool += rake
            BroadcastScores()
            hub.Broadcast("XT5_JACKPOT:" & jackpotPool.ToString())
            RefreshJackpotLabel()
        End If

        hub.Broadcast("XT5_LOCK:" & seat.ToString() & "|" & b.Amount.ToString() & "|0")
        RefreshPlayerCards()
        If game.AllActedThisStreet() Then AdvanceStreetOrShowdown()
    End Sub

    ''' <summary>Host xử lý 1 lượt BỎ BÀI gửi lên từ Client (hoặc từ chính Host).</summary>
    Private Sub ProcessFoldFromSeat(seat As Integer)
        If state <> RoundState.Betting Then
            RejectAction(seat)
            Return
        End If
        If Not game.CurrentBets.ContainsKey(seat) OrElse game.CurrentBets(seat).Folded OrElse game.CurrentBets(seat).ActedThisStreet Then
            RejectAction(seat)
            Return
        End If
        DoFoldSeat(seat)
        If game.AllActedThisStreet() Then AdvanceStreetOrShowdown()
    End Sub

    Private Sub RejectAction(seat As Integer)
        If seat = 0 Then
            UnlockLocalActionUI()
        Else
            hub.SendToClient(seat, "XT5_ACT_NACK:")
        End If
    End Sub

    Private Sub UnlockLocalActionUI()
        hasActedThisStreet = False
        If state = RoundState.Betting Then
            btnLockBet.Enabled = True
            btnFold.Enabled = True
            nudBet.Enabled = True
        End If
        AppendChat("[Hệ thống] Hành động vừa rồi không hợp lệ, hãy thử lại.")
    End Sub

    Private Sub BtnLockBet_Click(sender As Object, e As EventArgs)
        If state <> RoundState.Betting Then Return
        If hasActedThisStreet Then Return
        Dim amount As Long = CLng(nudBet.Value)
        Dim mySeat As Integer = If(isHost, 0, localSeat)
        If mySeat >= 0 AndAlso perSeatBetAmount(mySeat) + amount > scoresBySeat(mySeat) Then
            MessageBox.Show("Bạn không đủ điểm để cược thêm mức này.")
            Return
        End If
        hasActedThisStreet = True
        btnLockBet.Enabled = False
        btnFold.Enabled = False
        nudBet.Enabled = False

        If isHost Then
            ProcessRaiseFromSeat(0, amount)
        Else
            If peer IsNot Nothing AndAlso peer.IsConnected Then
                peer.SendLine("XT5_RAISE:" & amount.ToString())
            End If
        End If
    End Sub

    Private Sub BtnFold_Click(sender As Object, e As EventArgs)
        If state <> RoundState.Betting Then Return
        If hasActedThisStreet Then Return
        hasActedThisStreet = True
        btnLockBet.Enabled = False
        btnFold.Enabled = False
        nudBet.Enabled = False

        If isHost Then
            ProcessFoldFromSeat(0)
        Else
            If peer IsNot Nothing AndAlso peer.IsConnected Then
                peer.SendLine("XT5_FOLD:")
            End If
        End If
    End Sub

    ''' <summary>Chỉ Host gọi: đủ 5 lá (hoặc chỉ còn <= 1 người chưa Bỏ), so bài + tính điểm rồi
    ''' broadcast kết quả.</summary>
    Private Sub DoShowdown()
        If Not isHost Then Return
        state = RoundState.Revealing
        btnHostAction.Enabled = False
        btnLockBet.Enabled = False
        btnFold.Enabled = False
        nudBet.Enabled = False

        Dim outcomes As List(Of XiTo5LaGame.RoundOutcome) = game.ComputeShowdown(scoresBySeat)

        ' Jackpot: ai ra Sảnh Rồng (category 9) thì ăn JACKPOT_PAYOUT_PERCENT quỹ hiện có, phần còn
        ' lại giữ nguyên trong quỹ. Nếu (cực hiếm) nhiều người cùng ra Sảnh Rồng, chia đều phần thưởng.
        Dim jackpotBonusBySeat As New Dictionary(Of Integer, Long)
        Dim jackpotWinners As New List(Of Integer)
        Dim ow As XiTo5LaGame.RoundOutcome
        For Each ow In outcomes
            If ow.Hand.Category = 9 Then jackpotWinners.Add(ow.Seat)
        Next ow
        If jackpotWinners.Count > 0 AndAlso jackpotPool > 0 Then
            Dim totalBonus As Long = CLng(Math.Floor(jackpotPool * JACKPOT_PAYOUT_PERCENT))
            Dim perWinner As Long = totalBonus \ jackpotWinners.Count
            If perWinner > 0 Then
                Dim wSeat As Integer
                For Each wSeat In jackpotWinners
                    jackpotBonusBySeat(wSeat) = perWinner
                    scoresBySeat(wSeat) = scoresBySeat(wSeat) + perWinner
                    jackpotPool -= perWinner
                Next wSeat
            End If
        End If

        Dim sb As New StringBuilder()
        Dim first As Boolean = True
        Dim o As XiTo5LaGame.RoundOutcome
        For Each o In outcomes
            If Not first Then sb.Append(";")
            first = False
            Dim cardsJoined As String = String.Join("_", {o.Cards(0).WireCode(), o.Cards(1).WireCode(), o.Cards(2).WireCode(), o.Cards(3).WireCode(), o.Cards(4).WireCode()})
            Dim jb As Long = 0
            If jackpotBonusBySeat.ContainsKey(o.Seat) Then jb = jackpotBonusBySeat(o.Seat)
            sb.Append(o.Seat.ToString()).Append("|").Append(o.Amount.ToString()).Append("|")
            sb.Append(cardsJoined).Append("|").Append(o.Hand.Category.ToString()).Append("|")
            sb.Append(o.Payout.ToString()).Append("|").Append(scoresBySeat(o.Seat).ToString()).Append("|").Append(jb.ToString())
        Next o

        Dim entries As String = sb.ToString()
        lastRevealEntriesRaw = entries
        hub.Broadcast("XT5_REVEAL:" & entries)
        hub.Broadcast("XT5_JACKPOT:" & jackpotPool.ToString())
        RefreshJackpotLabel()

        BroadcastScores()
        StartRevealAnimation(entries)
    End Sub

    ' ============================================================
    '  LẬT BÀI + KẾT QUẢ
    ' ============================================================
    ''' <summary>Phân tích chuỗi entries nhận từ XT5_REVEAL (chỉ chứa những seat KHÔNG Fold),
    ''' lưu vào perSeatXxx rồi chạy hiệu ứng lật bài: lá của MÌNH lật lần lượt trước, sau đó lật
    ''' hết bài của đối thủ + hiện kết quả.</summary>
    Private Sub StartRevealAnimation(entriesRaw As String)
        state = RoundState.Revealing
        Dim i As Integer
        For i = 0 To 3
            If Not perSeatFolded(i) Then
                perSeatCards(i) = Nothing
                perSeatCategory(i) = -1
                perSeatJackpotBonus(i) = 0
            End If
        Next i

        If entriesRaw IsNot Nothing AndAlso entriesRaw.Trim() <> "" Then
            Dim entries As String() = entriesRaw.Split(";"c)
            Dim e As String
            For Each e In entries
                Dim f As String() = e.Split("|"c)
                Dim seat As Integer = Integer.Parse(f(0), CultureInfo.InvariantCulture)
                Dim amount As Long = Long.Parse(f(1), CultureInfo.InvariantCulture)
                Dim cardCodes As String() = f(2).Split("_"c)
                Dim cards(4) As XiTo5LaGame.CardInfo
                Dim k As Integer
                For k = 0 To 4
                    cards(k) = XiTo5LaGame.CardInfo.FromWireCode(cardCodes(k))
                Next k
                Dim category As Integer = Integer.Parse(f(3), CultureInfo.InvariantCulture)
                Dim payout As Long = Long.Parse(f(4), CultureInfo.InvariantCulture)
                Dim newScore As Long = Long.Parse(f(5), CultureInfo.InvariantCulture)
                Dim jackpotBonus As Long = 0
                If f.Length > 6 Then jackpotBonus = Long.Parse(f(6), CultureInfo.InvariantCulture)

                perSeatBetAmount(seat) = amount
                perSeatCards(seat) = cards
                perSeatCategory(seat) = category
                perSeatPayout(seat) = payout
                perSeatJackpotBonus(seat) = jackpotBonus
                scoresBySeat(seat) = newScore
            Next e
        End If

        lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - đang lật bài..."
        revealStep = 0
        If revealTimer Is Nothing Then
            revealTimer = New Timer()
            revealTimer.Interval = 220
            AddHandler revealTimer.Tick, AddressOf RevealTimer_Tick
        End If
        revealTimer.Start()
    End Sub

    Private Sub RevealTimer_Tick(sender As Object, e As EventArgs)
        revealStep += 1
        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        Dim mySeat As Integer = If(isHost, 0, localSeat)
        If mySeat >= 0 AndAlso pnlCardsRow(mySeat) IsNot Nothing Then pnlCardsRow(mySeat).Invalidate()

        If revealStep >= 6 Then
            revealTimer.Stop()
            FinishReveal()
        End If
    End Sub

    Private Sub FinishReveal()
        state = RoundState.ShowingResult
        AddRoundLogEntries()

        Dim i As Integer
        For i = 0 To 3
            If pnlCardsRow(i) IsNot Nothing Then pnlCardsRow(i).Invalidate()
            If lblCardResult(i) IsNot Nothing Then
                If perSeatFolded(i) Then
                    lblCardResult(i).Text = "Đã bỏ bài — mất " & perSeatBetAmount(i).ToString() & " điểm"
                    lblCardResult(i).ForeColor = Color.FromArgb(190, 30, 30)
                ElseIf perSeatCards(i) IsNot Nothing Then
                    Dim payout As Long = perSeatPayout(i)
                    Dim outcomeText As String
                    If payout > 0 Then
                        outcomeText = "Thắng +" & payout.ToString()
                    ElseIf payout < 0 Then
                        outcomeText = "Thua " & payout.ToString()
                    Else
                        outcomeText = "Hoà"
                    End If
                    lblCardResult(i).Text = XiTo5LaGame.CategoryNames(perSeatCategory(i)) & " — " & outcomeText
                    If perSeatJackpotBonus(i) > 0 Then
                        lblCardResult(i).Text &= "  🎰 +" & perSeatJackpotBonus(i).ToString() & " Jackpot!"
                    End If
                    lblCardResult(i).ForeColor = If(payout > 0, Color.FromArgb(20, 130, 40), If(payout < 0, Color.FromArgb(190, 30, 30), Color.DimGray))
                Else
                    lblCardResult(i).Text = "(không tham gia ván này)"
                    lblCardResult(i).ForeColor = Color.Gray
                End If
            End If
        Next i

        Dim rr As Integer
        For rr = 0 To 3
            If Not perSeatFolded(rr) AndAlso perSeatCards(rr) IsNot Nothing AndAlso perSeatCategory(rr) = 9 Then
                Dim announce As String = "🎉🐉 [SẢNH RỒNG] " & playerNames(rr) & " vừa ra bài Sảnh Rồng"
                If perSeatJackpotBonus(rr) > 0 Then
                    announce &= " và ẵm trọn 🎰 Jackpot +" & perSeatJackpotBonus(rr).ToString() & " điểm!"
                Else
                    announce &= "!"
                End If
                AppendChat("[Hệ thống] " & announce)
            End If
        Next rr

        Dim mySeat As Integer = If(isHost, 0, localSeat)
        If mySeat >= 0 AndAlso perSeatFolded(mySeat) Then
            lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - Bạn đã bỏ bài, mất " & perSeatBetAmount(mySeat).ToString() & " điểm."
        ElseIf mySeat >= 0 AndAlso perSeatCards(mySeat) IsNot Nothing Then
            Dim myPayout As Long = perSeatPayout(mySeat)
            Dim jackpotSuffix As String = ""
            If perSeatJackpotBonus(mySeat) > 0 Then
                jackpotSuffix = " 🎰 Bạn còn ăn thêm " & perSeatJackpotBonus(mySeat).ToString() & " điểm Jackpot!"
            End If
            If myPayout > 0 Then
                lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - Bạn có " & XiTo5LaGame.CategoryNames(perSeatCategory(mySeat)) & ", THẮNG +" & myPayout.ToString() & " điểm!" & jackpotSuffix
            ElseIf myPayout < 0 Then
                lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - Bạn có " & XiTo5LaGame.CategoryNames(perSeatCategory(mySeat)) & ", thua " & myPayout.ToString() & " điểm." & jackpotSuffix
            Else
                lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - Bạn có " & XiTo5LaGame.CategoryNames(perSeatCategory(mySeat)) & ", hoà không đổi điểm." & jackpotSuffix
            End If
        Else
            lblRoundInfo.Text = "Ván " & game.CurrentRoundNo.ToString() & " - kết thúc so bài."
        End If

        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        If pnlRoundBanner IsNot Nothing Then pnlRoundBanner.Invalidate()

        If isHost Then
            btnHostAction.Text = "Bắt đầu ván mới"
            btnHostAction.Enabled = True
            btnHostAction.Visible = True
        End If
        btnLockBet.Enabled = False
        btnFold.Enabled = False
        nudBet.Enabled = False
        RefreshPlayerCards()
        StartSparkleIfAny()
    End Sub

    ''' <summary>Kiểm tra xem có seat nào (không Fold) ra bài từ Tứ quý trở lên không; nếu có thì
    ''' bật timer chạy hiệu ứng lấp lánh (viền phát sáng nhấp nháy + tia sao) quanh bộ bài của họ.</summary>
    Private Sub StartSparkleIfAny()
        Dim any As Boolean = False
        Dim i As Integer
        For i = 0 To 3
            If Not perSeatFolded(i) AndAlso perSeatCards(i) IsNot Nothing AndAlso perSeatCategory(i) >= SPARKLE_MIN_CATEGORY Then
                any = True
            End If
            If perSeatJackpotBonus(i) > 0 Then
                perSeatJackpotBurstStep(i) = 0  ' bắt đầu phát hiệu ứng nổ Jackpot 1 lần cho seat này
                any = True
            End If
        Next i
        If Not any Then Return

        If sparkleTimer Is Nothing Then
            sparkleTimer = New Timer()
            sparkleTimer.Interval = 50
            AddHandler sparkleTimer.Tick, AddressOf SparkleTimer_Tick
        End If
        sparklePhase = 0.0
        sparkleTimer.Start()
    End Sub

    Private Sub SparkleTimer_Tick(sender As Object, e As EventArgs)
        sparklePhase += 0.18
        Dim i As Integer
        For i = 0 To 3
            If perSeatJackpotBurstStep(i) >= 0 AndAlso perSeatJackpotBurstStep(i) < JACKPOT_FRAME_COUNT Then
                perSeatJackpotBurstStep(i) += 1  ' chạy tuần tự từng khung hiệu ứng nổ, dừng khi hết khung
            End If
        Next i
        If pnlMyHand IsNot Nothing Then pnlMyHand.Invalidate()
        For i = 0 To 3
            If pnlCardsRow(i) IsNot Nothing Then pnlCardsRow(i).Invalidate()
        Next i
    End Sub

    Private Sub StopSparkle()
        If sparkleTimer IsNot Nothing Then sparkleTimer.Stop()
    End Sub

    ''' <summary>Ghi log ván này vào nhật ký (chỉ cần làm 1 lần trên mỗi máy, dựa vào dữ liệu perSeat*
    ''' đã có sẵn cục bộ - không cần đợi thêm gói tin nào).</summary>
    Private Sub AddRoundLogEntries()
        Dim bestSeat As Integer = -1
        Dim bestCategory As Integer = -1
        Dim foldedCount As Integer = 0
        Dim s As Integer
        For s = 0 To 3
            If perSeatFolded(s) Then foldedCount += 1
            If perSeatCards(s) IsNot Nothing Then
                If perSeatCategory(s) > bestCategory Then
                    bestCategory = perSeatCategory(s)
                    bestSeat = s
                End If
            End If
        Next s
        Dim line As String
        If bestSeat >= 0 Then
            line = "Ván " & game.CurrentRoundNo.ToString() & ": " & playerNames(bestSeat) & " có bài mạnh nhất (" & XiTo5LaGame.CategoryNames(bestCategory) & ")"
            If foldedCount > 0 Then line &= ", " & foldedCount.ToString() & " người đã bỏ bài"
        ElseIf foldedCount > 0 Then
            line = "Ván " & game.CurrentRoundNo.ToString() & ": chỉ còn 1 người, không ai so bài (những người khác đã bỏ)."
        Else
            line = "Ván " & game.CurrentRoundNo.ToString() & ": không ai tham gia."
        End If
        Dim js As Integer
        For js = 0 To 3
            If perSeatJackpotBonus(js) > 0 Then
                line &= " 🎰 " & playerNames(js) & " trúng Jackpot +" & perSeatJackpotBonus(js).ToString() & " điểm!"
            End If
        Next js
        roundLog.Insert(0, line)
        If roundLog.Count > HISTORY_MAX Then roundLog.RemoveAt(roundLog.Count - 1)
        RefreshHistoryList()
    End Sub

    Private Sub RefreshHistoryList()
        If lstHistory Is Nothing Then Return
        lstHistory.Items.Clear()
        Dim line As String
        For Each line In roundLog
            lstHistory.Items.Add(line)
        Next line
    End Sub

    ' ============================================================
    '  XÂY DỰNG GIAO DIỆN GAME
    ' ============================================================
    Private Sub ShowGamePanel()
        If pnlGame IsNot Nothing Then Return
        pnlConnect.Visible = False

        pnlGame = New Panel()
        pnlGame.Dock = DockStyle.Fill
        pnlGame.BackColor = Color.FromArgb(20, 24, 30)

        Const BANNER_H As Integer = 40
        Const BANNER_GAP As Integer = 8
        Dim boardTopY As Integer = 20 + BANNER_H + BANNER_GAP

        pnlRoundBanner = New Panel()
        pnlRoundBanner.Location = New Point(20, 20)
        pnlRoundBanner.Size = New Size(BOARD_W, BANNER_H)
        pnlRoundBanner.BackColor = Color.FromArgb(20, 24, 30)
        AddHandler pnlRoundBanner.Paint, AddressOf RoundBanner_Paint
        pnlGame.Controls.Add(pnlRoundBanner)

        pnlMyHand = New Panel()
        pnlMyHand.Location = New Point(20, boardTopY)
        pnlMyHand.Size = New Size(BOARD_W, BOARD_H)
        pnlMyHand.BackColor = Color.FromArgb(10, 40, 30)
        pnlMyHand.BorderStyle = BorderStyle.FixedSingle
        AddHandler pnlMyHand.Paint, AddressOf MyHand_Paint
        pnlGame.Controls.Add(pnlMyHand)

        lblRoundInfo = New Label()
        lblRoundInfo.Location = New Point(20, boardTopY + BOARD_H + 10) : lblRoundInfo.AutoSize = True
        lblRoundInfo.ForeColor = Color.White
        lblRoundInfo.Font = New Font("Segoe UI", 10.0!, FontStyle.Bold)
        lblRoundInfo.MaximumSize = New Size(BOARD_W, 0)
        lblRoundInfo.Text = "Chờ Host bắt đầu ván mới..."
        pnlGame.Controls.Add(lblRoundInfo)

        lblCountdown = New Label()
        lblCountdown.Location = New Point(20, boardTopY + BOARD_H + 35) : lblCountdown.AutoSize = True
        lblCountdown.ForeColor = Color.Gold
        lblCountdown.Font = New Font("Segoe UI", 10.0!)
        pnlGame.Controls.Add(lblCountdown)

        Dim lblBetCap As New Label()
        lblBetCap.Text = "Cược thêm (" & XiTo5LaGame.MIN_BET.ToString() & "-" & XiTo5LaGame.MAX_BET.ToString() & "):"
        lblBetCap.AutoSize = True
        lblBetCap.ForeColor = Color.White
        lblBetCap.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        lblBetCap.Location = New Point(20, boardTopY + BOARD_H + 72)
        pnlGame.Controls.Add(lblBetCap)

        nudBet = New NumericUpDown()
        nudBet.Location = New Point(195, boardTopY + BOARD_H + 66)
        nudBet.Size = New Size(75, 26)
        nudBet.Font = New Font("Segoe UI", 10.0!, FontStyle.Bold)
        nudBet.BackColor = Color.White
        nudBet.ForeColor = Color.Black
        nudBet.BorderStyle = BorderStyle.FixedSingle
        nudBet.TextAlign = HorizontalAlignment.Center
        nudBet.Minimum = CDec(XiTo5LaGame.MIN_BET)
        nudBet.Maximum = CDec(XiTo5LaGame.MAX_BET)
        nudBet.Increment = 10
        nudBet.Value = CDec(XiTo5LaGame.MIN_BET)
        nudBet.Enabled = False
        pnlGame.Controls.Add(nudBet)

        btnLockBet = New Button()
        btnLockBet.Text = "Cược thêm"
        btnLockBet.Location = New Point(280, boardTopY + BOARD_H + 64) : btnLockBet.Size = New Size(95, 30)
        btnLockBet.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        btnLockBet.FlatStyle = FlatStyle.Flat
        btnLockBet.FlatAppearance.BorderSize = 0
        btnLockBet.BackColor = Color.FromArgb(46, 160, 67)
        btnLockBet.ForeColor = Color.White
        btnLockBet.Enabled = False
        AddHandler btnLockBet.Click, AddressOf BtnLockBet_Click
        pnlGame.Controls.Add(btnLockBet)

        btnFold = New Button()
        btnFold.Text = "Bỏ bài (Fold)"
        btnFold.Location = New Point(380, boardTopY + BOARD_H + 64) : btnFold.Size = New Size(105, 30)
        btnFold.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        btnFold.FlatStyle = FlatStyle.Flat
        btnFold.FlatAppearance.BorderSize = 0
        btnFold.BackColor = Color.FromArgb(190, 60, 50)
        btnFold.ForeColor = Color.White
        btnFold.Enabled = False
        AddHandler btnFold.Click, AddressOf BtnFold_Click
        pnlGame.Controls.Add(btnFold)

        btnHostAction = New Button()
        btnHostAction.Text = "Bắt đầu ván mới"
        btnHostAction.Location = New Point(20, boardTopY + BOARD_H + 100) : btnHostAction.Size = New Size(200, 30)
        btnHostAction.Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        btnHostAction.FlatStyle = FlatStyle.Flat
        btnHostAction.FlatAppearance.BorderSize = 0
        btnHostAction.BackColor = Color.FromArgb(41, 121, 255)
        btnHostAction.ForeColor = Color.White
        btnHostAction.Visible = isHost
        AddHandler btnHostAction.Click, AddressOf BtnHostAction_Click
        pnlGame.Controls.Add(btnHostAction)

        Dim lblHistoryTitle As New Label()
        lblHistoryTitle.Text = "Nhật ký ván đấu (gần đây nhất ở trên):"
        lblHistoryTitle.AutoSize = True
        lblHistoryTitle.ForeColor = Color.White
        lblHistoryTitle.Font = New Font("Segoe UI", 9.0!, FontStyle.Bold)
        lblHistoryTitle.Location = New Point(20, boardTopY + BOARD_H + 138)
        pnlGame.Controls.Add(lblHistoryTitle)

        lstHistory = New ListBox()
        lstHistory.Location = New Point(20, boardTopY + BOARD_H + 160)
        lstHistory.Size = New Size(BOARD_W, 96)
        lstHistory.BackColor = Color.FromArgb(12, 16, 20)
        lstHistory.ForeColor = Color.Gainsboro
        lstHistory.BorderStyle = BorderStyle.FixedSingle
        pnlGame.Controls.Add(lstHistory)
        RefreshHistoryList()

        Dim sideX As Integer = BOARD_W + 40
        Dim p As Integer
        For p = 0 To 3
            pnlPlayers(p) = BuildPlayerCard(p, New Point(sideX, 20 + p * 110), 300)
            pnlGame.Controls.Add(pnlPlayers(p))
            If isHost AndAlso p <> 0 Then AttachTopUpMenu(pnlPlayers(p), p)
        Next p

        If isHost Then
            Dim lblTopUpHint As New Label()
            lblTopUpHint.Text = "Mẹo: bấm CHUỘT PHẢI vào thẻ 1 người chơi để nạp hoặc trừ điểm cho họ (đổi ngược với điểm của Host)."
            lblTopUpHint.AutoSize = True
            lblTopUpHint.ForeColor = Color.LightGray
            lblTopUpHint.Font = New Font("Segoe UI", 7.5!, FontStyle.Italic)
            lblTopUpHint.MaximumSize = New Size(300, 0)
            lblTopUpHint.Location = New Point(sideX, 20 + 4 * 110 + 2)
            pnlGame.Controls.Add(lblTopUpHint)
        End If

        BuildChatPanel(sideX, 300, 20 + 4 * 110 + 40, 700 - (20 + 4 * 110 + 40) - 20)

        Me.Controls.Add(pnlGame)
        RefreshPlayerCards()
    End Sub

    Private Function BuildPlayerCard(player As Integer, loc As Point, w As Integer) As Panel
        Dim card As New Panel()
        card.Location = loc : card.Size = New Size(w, 104)
        card.BackColor = Color.White
        card.BorderStyle = BorderStyle.FixedSingle

        Dim bar As New Panel()
        bar.Location = New Point(0, 0) : bar.Size = New Size(6, 104)
        bar.BackColor = PlayerColor(player)
        card.Controls.Add(bar)

        lblCardTitle(player) = New Label()
        lblCardTitle(player).Font = New Font("Segoe UI", 9.5!, FontStyle.Bold)
        lblCardTitle(player).ForeColor = PlayerColor(player)
        lblCardTitle(player).Location = New Point(16, 4) : lblCardTitle(player).AutoSize = True
        card.Controls.Add(lblCardTitle(player))

        lblCardStatus(player) = New Label()
        lblCardStatus(player).Font = New Font("Segoe UI", 8.5!)
        lblCardStatus(player).ForeColor = Color.DimGray
        lblCardStatus(player).Location = New Point(16, 22) : lblCardStatus(player).AutoSize = True
        card.Controls.Add(lblCardStatus(player))

        pnlCardsRow(player) = New Panel()
        pnlCardsRow(player).Location = New Point(16, 42)
        pnlCardsRow(player).Size = New Size(268, 42)
        pnlCardsRow(player).BackColor = Color.White
        pnlCardsRow(player).Tag = player
        AddHandler pnlCardsRow(player).Paint, AddressOf CardsRow_Paint
        card.Controls.Add(pnlCardsRow(player))

        lblCardResult(player) = New Label()
        lblCardResult(player).Font = New Font("Segoe UI", 8.5!, FontStyle.Bold)
        lblCardResult(player).ForeColor = Color.Gray
        lblCardResult(player).Text = ""
        lblCardResult(player).Location = New Point(16, 86) : lblCardResult(player).AutoSize = True
        card.Controls.Add(lblCardResult(player))

        Return card
    End Function

    ' ============================================================
    '  VẼ BÀI (Panel.Paint), dùng chung cho hàng bài của từng người chơi và bàn tay của MÌNH
    ' ============================================================
    Private Sub RoundBanner_Paint(sender As Object, e As EventArgs)
        Dim pe As PaintEventArgs = CType(e, PaintEventArgs)
        Dim g As Graphics = pe.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias
        Dim rect As New Rectangle(0, 0, pnlRoundBanner.Width - 1, pnlRoundBanner.Height - 1)
        Using bg As New SolidBrush(Color.FromArgb(35, 24, 10))
            g.FillRectangle(bg, rect)
        End Using
        Using borderPen As New Pen(Color.FromArgb(210, 170, 60), 2)
            g.DrawRectangle(borderPen, rect)
        End Using
        Dim txt As String = "Ván " & game.CurrentRoundNo.ToString() & " — " & StateLabel()
        Using textBrush As New SolidBrush(Color.Gold)
            g.DrawString(txt, New Font("Segoe UI", 11.0!, FontStyle.Bold), textBrush, 12, pnlRoundBanner.Height / 2.0F - 10)
        End Using
        Dim jpTxt As String = "🎰 Jackpot: " & jackpotPool.ToString() & " điểm"
        Using jpFont As New Font("Segoe UI", 9.5!, FontStyle.Bold)
            Dim jpSize As SizeF = g.MeasureString(jpTxt, jpFont)
            Using jpBrush As New SolidBrush(Color.FromArgb(255, 225, 140))
                g.DrawString(jpTxt, jpFont, jpBrush, pnlRoundBanner.Width - jpSize.Width - 12, pnlRoundBanner.Height / 2.0F - jpSize.Height / 2.0F)
            End Using
        End Using
    End Sub

    ''' <summary>Vẽ lại banner để cập nhật số điểm Jackpot hiển thị (gọi mỗi khi jackpotPool đổi).</summary>
    Private Sub RefreshJackpotLabel()
        If pnlRoundBanner IsNot Nothing Then pnlRoundBanner.Invalidate()
    End Sub

    Private Function StateLabel() As String
        Select Case state
            Case RoundState.Idle : Return "chờ bắt đầu"
            Case RoundState.Betting : Return "đợt cược " & game.CurrentStreet.ToString() & "/" & XiTo5LaGame.STREET_COUNT.ToString()
            Case RoundState.Revealing : Return "đang lật bài"
            Case Else : Return "đã có kết quả"
        End Select
    End Function

    ''' <summary>Vẽ 5 lá bài lớn của MÌNH ở khu vực bàn chính. Trong lúc đang chơi (chưa showdown),
    ''' chỉ vẽ đúng số lá mình đã được chia (perSeatCardCount), luôn lật mặt vì đó là bài của mình.</summary>
    Private Sub MyHand_Paint(sender As Object, e As EventArgs)
        Dim pe As PaintEventArgs = CType(e, PaintEventArgs)
        Dim g As Graphics = pe.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Dim mySeat As Integer = If(isHost, 0, localSeat)
        Dim cardW As Integer = 90
        Dim cardH As Integer = 130
        Dim gap As Integer = 14
        Dim totalW As Integer = cardW * 5 + gap * 4
        Dim startX As Integer = (pnlMyHand.Width - totalW) \ 2
        Dim startY As Integer = (pnlMyHand.Height - cardH) \ 2

        If mySeat < 0 Then
            DrawCenterText(g, pnlMyHand.ClientRectangle, "Đang chờ vào phòng...", Color.White)
            Return
        End If

        Dim hasBet As Boolean = (perSeatBetAmount(mySeat) >= 0)
        Dim showdownCards As XiTo5LaGame.CardInfo() = perSeatCards(mySeat)
        Dim myList As List(Of XiTo5LaGame.CardInfo) = perSeatMyCardList(mySeat)

        Dim i As Integer
        For i = 0 To 4
            Dim rect As New Rectangle(startX + i * (cardW + gap), startY, cardW, cardH)
            If showdownCards IsNot Nothing Then
                Dim showFace As Boolean = (revealStep > i OrElse state = RoundState.ShowingResult)
                If showFace Then
                    DrawCard(g, rect, showdownCards(i), True)
                Else
                    DrawCard(g, rect, Nothing, False)
                End If
            ElseIf myList IsNot Nothing AndAlso i < myList.Count Then
                DrawCard(g, rect, myList(i), True) ' bài của MÌNH luôn lật ngay khi được chia
            ElseIf perSeatFolded(mySeat) Then
                DrawCard(g, rect, Nothing, False)
            Else
                DrawEmptySlot(g, rect, hasBet)
            End If
        Next i

        If state = RoundState.ShowingResult AndAlso Not perSeatFolded(mySeat) AndAlso perSeatCategory(mySeat) >= SPARKLE_MIN_CATEGORY Then
            Dim glowRect As New Rectangle(startX - 10, startY - 10, totalW + 20, cardH + 20)
            DrawSparkleOverlay(g, glowRect, perSeatCategory(mySeat), mySeat, sparklePhase)
        End If

        If perSeatFolded(mySeat) Then
            Using f As New SolidBrush(Color.FromArgb(220, 255, 120, 120))
                g.DrawString("Bạn đã bỏ bài — mất " & perSeatBetAmount(mySeat).ToString() & " điểm", New Font("Segoe UI", 9.5!, FontStyle.Italic), f, startX, startY + cardH + 8)
            End Using
        ElseIf state = RoundState.Betting AndAlso Not perSeatActedThisStreet(mySeat) AndAlso myList IsNot Nothing Then
            Using f As New SolidBrush(Color.FromArgb(200, 255, 255, 255))
                g.DrawString("Hãy ""Cược thêm"" hoặc ""Bỏ bài""", New Font("Segoe UI", 9.5!, FontStyle.Italic), f, startX, startY + cardH + 8)
            End Using
        End If
    End Sub

    ''' <summary>Vẽ hàng 5 lá bài nhỏ trong thẻ 1 người chơi (Panel.Paint, Tag = số seat). Trong
    ''' lúc đang chơi chỉ vẽ mặt SAU đúng số lá người đó đang có (perSeatCardCount); tới khi
    ''' showdown mới lật theo revealStep giống trước.</summary>
    Private Sub CardsRow_Paint(sender As Object, e As EventArgs)
        Dim panelSender As Panel = CType(sender, Panel)
        Dim seat As Integer = CInt(panelSender.Tag)
        Dim pe As PaintEventArgs = CType(e, PaintEventArgs)
        Dim g As Graphics = pe.Graphics
        g.SmoothingMode = SmoothingMode.AntiAlias

        Dim cardW As Integer = 30
        Dim cardH As Integer = 42
        Dim gap As Integer = 3
        Dim hasBet As Boolean = (perSeatBetAmount(seat) >= 0)
        Dim showdownCards As XiTo5LaGame.CardInfo() = perSeatCards(seat)
        Dim cardCount As Integer = perSeatCardCount(seat)

        Dim mySeat As Integer = If(isHost, 0, localSeat)
        Dim i As Integer
        For i = 0 To 4
            Dim rect As New Rectangle(i * (cardW + gap), 0, cardW, cardH)
            If showdownCards IsNot Nothing Then
                Dim showFace As Boolean
                If seat = mySeat Then
                    showFace = (revealStep > i OrElse state = RoundState.ShowingResult)
                Else
                    showFace = (revealStep >= 6 OrElse state = RoundState.ShowingResult)
                End If
                If showFace Then
                    DrawCard(g, rect, showdownCards(i), True)
                Else
                    DrawCard(g, rect, Nothing, False)
                End If
            ElseIf i < cardCount Then
                DrawCard(g, rect, Nothing, False) ' đang chơi: chỉ biết đối thủ có bao nhiêu lá, chưa lộ mặt
            ElseIf perSeatFolded(seat) Then
                DrawEmptySlot(g, rect, False)
            Else
                DrawEmptySlot(g, rect, hasBet)
            End If
        Next i

        If state = RoundState.ShowingResult AndAlso Not perSeatFolded(seat) AndAlso perSeatCategory(seat) >= SPARKLE_MIN_CATEGORY Then
            Dim glowRect As New Rectangle(-4, -4, 5 * (cardW + gap) + 4, cardH + 8)
            DrawSparkleOverlay(g, glowRect, perSeatCategory(seat), seat, sparklePhase)
        End If
    End Sub

    ''' <summary>Vẽ 1 lá bài. Nếu faceUp=True và card IsNot Nothing thì vẽ mặt trước (sprite hoặc
    ''' fallback chữ+ký hiệu), ngược lại vẽ mặt sau (sprite hoặc fallback màu xanh đậm).</summary>
    Private Sub DrawCard(g As Graphics, rect As Rectangle, card As XiTo5LaGame.CardInfo, faceUp As Boolean)
        If faceUp AndAlso card IsNot Nothing Then
            Dim img As Image = Nothing
            If cardImages.ContainsKey(card.Code()) Then img = cardImages(card.Code())
            If img IsNot Nothing Then
                g.DrawImage(img, rect)
            Else
                Using bg As New SolidBrush(Color.White)
                    g.FillRectangle(bg, rect)
                End Using
                Using border As New Pen(Color.FromArgb(60, 60, 60), 1)
                    g.DrawRectangle(border, rect)
                End Using
                Dim isRed As Boolean = (card.Suit = XiTo5LaGame.CardSuit.Ro OrElse card.Suit = XiTo5LaGame.CardSuit.Co)
                Dim inkColor As Color = If(isRed, Color.FromArgb(200, 20, 20), Color.FromArgb(20, 20, 20))
                Dim rankFont As New Font("Segoe UI", CSng(Math.Max(7, rect.Width \ 4)), FontStyle.Bold)
                Dim suitFont As New Font("Segoe UI", CSng(Math.Max(10, rect.Width \ 3)), FontStyle.Bold)
                Using ink As New SolidBrush(inkColor)
                    g.DrawString(card.RankLabel(), rankFont, ink, rect.X + 2, rect.Y + 1)
                    Dim sSize As SizeF = g.MeasureString(card.SuitSymbol(), suitFont)
                    g.DrawString(card.SuitSymbol(), suitFont, ink, rect.X + (rect.Width - sSize.Width) / 2.0F, rect.Y + rect.Height - sSize.Height - 2)
                End Using
            End If
        Else
            If cardBackImage IsNot Nothing Then
                g.DrawImage(cardBackImage, rect)
            Else
                Using bg As New SolidBrush(Color.FromArgb(30, 50, 110))
                    g.FillRectangle(bg, rect)
                End Using
                Using border As New Pen(Color.FromArgb(200, 180, 90), 1)
                    g.DrawRectangle(border, rect)
                End Using
                Using inner As New Pen(Color.FromArgb(80, 110, 180), 1)
                    Dim pad As Integer = 4
                    g.DrawRectangle(inner, rect.X + pad, rect.Y + pad, rect.Width - pad * 2, rect.Height - pad * 2)
                End Using
            End If
        End If
    End Sub

    ''' <summary>Vẽ 1 ô trống (chưa được chia bài): viền chấm mờ, đậm hơn nếu người đó đã khoá cược.</summary>
    Private Sub DrawEmptySlot(g As Graphics, rect As Rectangle, hasBet As Boolean)
        Dim penColor As Color = If(hasBet, Color.FromArgb(160, 210, 170, 60), Color.FromArgb(70, 200, 200, 200))
        Using dashPen As New Pen(penColor, 1)
            dashPen.DashStyle = DashStyle.Dash
            g.DrawRectangle(dashPen, rect)
        End Using
    End Sub

    Private Sub DrawCenterText(g As Graphics, rect As Rectangle, text As String, c As Color)
        Using f As New Font("Segoe UI", 10.0!, FontStyle.Bold)
            Dim sz As SizeF = g.MeasureString(text, f)
            Using b As New SolidBrush(c)
                g.DrawString(text, f, b, rect.X + (rect.Width - sz.Width) / 2.0F, rect.Y + (rect.Height - sz.Height) / 2.0F)
            End Using
        End Using
    End Sub

    ''' <summary>Vẽ hiệu ứng lấp lánh (viền phát sáng nhấp nháy + tia sao rải rác) quanh khu vực
    ''' 1 bộ bài, dùng khi kết quả là bài mạnh (Tứ quý trở lên, category &gt;= SPARKLE_MIN_CATEGORY).
    ''' Càng hiếm (Sảnh Rồng &gt; Thùng phá sảnh &gt; Tứ quý) thì màu càng vàng rực và càng nhiều tia.
    ''' Vị trí các tia được sinh từ Random có seed cố định (dựa trên category + kích thước vùng) nên
    ''' đứng yên qua từng khung hình, chỉ độ sáng nhấp nháy theo sparklePhase.</summary>
    ''' <summary>Vẽ hiệu ứng cho 1 bộ bài mạnh (category &gt;= SPARKLE_MIN_CATEGORY): Sảnh Rồng dùng
    ''' sprite rồng vàng lượn quanh (sanhrong.png), Tứ quý/Thùng phá sảnh dùng sprite lấp lánh
    ''' (sparkle.png), phát lặp liên tục theo sparklePhase. Nếu seat này vừa ăn Jackpot thì chồng
    ''' thêm hiệu ứng nổ xu vàng (jackpot_burst.png), chỉ phát 1 lần theo perSeatJackpotBurstStep.
    ''' Thiếu sprite nào thì rơi về vẽ tay GDI+ (viền phát sáng + tia sao) như bản cũ.</summary>
    Private Sub DrawSparkleOverlay(g As Graphics, areaRect As Rectangle, category As Integer, seat As Integer, phase As Double)
        If category >= SPARKLE_MIN_CATEGORY Then
            Dim sheet As Image = If(category = 9, spriteSanhRongSheet, spriteSparkleSheet)
            Dim frameCount As Integer = If(category = 9, SANHRONG_FRAME_COUNT, SPARKLE_FRAME_COUNT)

            If sheet IsNot Nothing Then
                Dim frameW As Integer = sheet.Width \ frameCount
                Dim frameH As Integer = sheet.Height
                Dim frameIdx As Integer = CInt(Math.Floor(phase * 2.5)) Mod frameCount
                Dim srcRect As New Rectangle(frameIdx * frameW, 0, frameW, frameH)
                Dim pad As Integer = CInt(areaRect.Width * 0.18)
                Dim destRect As New Rectangle(areaRect.X - pad, areaRect.Y - pad, areaRect.Width + pad * 2, areaRect.Height + pad * 2)
                g.DrawImage(sheet, destRect, srcRect, GraphicsUnit.Pixel)
            Else
                DrawSparkleFallback(g, areaRect, category, phase)
            End If
        End If

        ' Hiệu ứng nổ Jackpot (chỉ phát 1 lần khi vừa ăn, không lặp lại)
        If seat >= 0 AndAlso seat <= 3 AndAlso perSeatJackpotBurstStep(seat) >= 0 AndAlso perSeatJackpotBurstStep(seat) < JACKPOT_FRAME_COUNT AndAlso spriteJackpotSheet IsNot Nothing Then
            Dim jfw As Integer = spriteJackpotSheet.Width \ JACKPOT_FRAME_COUNT
            Dim jfh As Integer = spriteJackpotSheet.Height
            Dim jSrc As New Rectangle(perSeatJackpotBurstStep(seat) * jfw, 0, jfw, jfh)
            Dim jSize As Integer = CInt(areaRect.Height * 2.2)
            Dim jDest As New Rectangle(areaRect.X + areaRect.Width \ 2 - jSize \ 2, areaRect.Y + areaRect.Height \ 2 - jSize \ 2, jSize, jSize)
            g.DrawImage(spriteJackpotSheet, jDest, jSrc, GraphicsUnit.Pixel)
        End If
    End Sub

    ''' <summary>Hiệu ứng dự phòng vẽ tay bằng GDI+ (viền phát sáng nhấp nháy + tia sao), dùng khi
    ''' chưa có sprite sheet tương ứng trong Assets\Effects.</summary>
    Private Sub DrawSparkleFallback(g As Graphics, areaRect As Rectangle, category As Integer, phase As Double)
        Dim glowColor As Color
        Dim starCount As Integer
        Select Case category
            Case 9 : glowColor = Color.FromArgb(255, 215, 0) : starCount = 16   ' Sảnh Rồng: vàng rực
            Case 8 : glowColor = Color.FromArgb(255, 235, 130) : starCount = 11 ' Thùng phá sảnh: vàng nhạt
            Case Else : glowColor = Color.FromArgb(190, 220, 255) : starCount = 7 ' Tứ quý: bạc ánh xanh
        End Select

        Dim pulse As Single = CSng((Math.Sin(phase) + 1.0) / 2.0) ' 0..1 nhấp nháy
        Dim glowAlpha As Integer = 90 + CInt(pulse * 140)
        Using glowPen As New Pen(Color.FromArgb(Math.Min(255, glowAlpha), glowColor), 2.5F + pulse * 2.0F)
            g.DrawRectangle(glowPen, areaRect)
        End Using

        Dim rnd As New Random(category * 97 + areaRect.Width * 31 + areaRect.Height)
        Dim i As Integer
        For i = 0 To starCount - 1
            Dim px As Single = areaRect.X + CSng(rnd.NextDouble()) * areaRect.Width
            Dim py As Single = areaRect.Y + CSng(rnd.NextDouble()) * areaRect.Height
            Dim twinkle As Single = CSng((Math.Sin(phase * 2.0 + i * 1.3) + 1.0) / 2.0)
            Dim starAlpha As Integer = 50 + CInt(twinkle * 195)
            Dim starSize As Single = 2.0F + twinkle * (If(category = 9, 4.5F, 3.0F))

            Using glowBrush As New SolidBrush(Color.FromArgb(Math.Min(255, starAlpha \ 2), glowColor))
                g.FillEllipse(glowBrush, px - starSize * 1.6F, py - starSize * 1.6F, starSize * 3.2F, starSize * 3.2F)
            End Using
            Using starBrush As New SolidBrush(Color.FromArgb(Math.Min(255, starAlpha), Color.White))
                g.FillRectangle(starBrush, px - starSize, py - 0.6F, starSize * 2.0F, 1.2F)
                g.FillRectangle(starBrush, px - 0.6F, py - starSize, 1.2F, starSize * 2.0F)
            End Using
        Next i
    End Sub

    ' ============================================================
    '  NẠP / TRỪ ĐIỂM THỦ CÔNG (chỉ Host)
    ' ============================================================
    Private Sub AttachTopUpMenu(card As Panel, player As Integer)
        Dim cms As New ContextMenuStrip()

        Dim itemAdd As New ToolStripMenuItem("Nạp điểm cho người chơi này...")
        AddHandler itemAdd.Click, Sub(s As Object, e As EventArgs) AdjustPlayerScore(player, True)
        cms.Items.Add(itemAdd)

        Dim itemSub As New ToolStripMenuItem("Trừ điểm của người chơi này...")
        AddHandler itemSub.Click, Sub(s As Object, e As EventArgs) AdjustPlayerScore(player, False)
        cms.Items.Add(itemSub)

        card.ContextMenuStrip = cms
        Dim ctrl As Control
        For Each ctrl In card.Controls
            ctrl.ContextMenuStrip = cms
        Next ctrl
    End Sub

    Private Sub AdjustPlayerScore(seat As Integer, isTopUp As Boolean)
        If Not isHost Then Return
        If seat = 0 Then Return

        If Not playerConnected(seat) Then
            MessageBox.Show("Player " & (seat + 1).ToString() & " hiện chưa có ai, không thể chỉnh điểm.")
            Return
        End If

        Dim actionLabel As String = If(isTopUp, "nạp cho", "trừ của")
        Dim promptMsg As String = "Nhập số điểm muốn " & actionLabel & " Player " & (seat + 1).ToString() & " (" & playerNames(seat) & ")." & vbCrLf &
            If(isTopUp, "Số điểm này sẽ được TRỪ trực tiếp từ điểm của bạn (Host).", "Số điểm này sẽ được CỘNG trực tiếp vào điểm của bạn (Host).")
        Dim dialogTitle As String = If(isTopUp, "Nạp điểm cho người chơi", "Trừ điểm của người chơi")
        Dim raw As String = InputBox(promptMsg, dialogTitle, "50")
        If raw Is Nothing OrElse raw.Trim() = "" Then Return

        Dim amount As Long
        If Not Long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, amount) Then
            MessageBox.Show("Số điểm không hợp lệ.")
            Return
        End If
        If amount <= 0 Then
            MessageBox.Show("Số điểm phải lớn hơn 0.")
            Return
        End If

        Dim hostScore As Long = 0
        If scoresBySeat.ContainsKey(0) Then hostScore = scoresBySeat(0)
        Dim targetScore As Long = 0
        If scoresBySeat.ContainsKey(seat) Then targetScore = scoresBySeat(seat)

        Dim sysMsg As String
        If isTopUp Then
            If amount > hostScore Then
                MessageBox.Show("Bạn không đủ điểm để nạp (bạn đang có " & hostScore.ToString() & " điểm).")
                Return
            End If
            scoresBySeat(0) = hostScore - amount
            scoresBySeat(seat) = targetScore + amount
            sysMsg = "Host đã nạp " & amount.ToString() & " điểm cho Player " & (seat + 1).ToString() & " (" & playerNames(seat) & ")."
        Else
            If amount > targetScore Then
                MessageBox.Show("Player " & (seat + 1).ToString() & " không đủ điểm để trừ (hiện có " & targetScore.ToString() & " điểm).")
                Return
            End If
            scoresBySeat(seat) = targetScore - amount
            scoresBySeat(0) = hostScore + amount
            sysMsg = "Host đã trừ " & amount.ToString() & " điểm của Player " & (seat + 1).ToString() & " (" & playerNames(seat) & ")."
        End If

        AppendChat("Hệ thống: " & sysMsg)
        hub.Broadcast("CHAT:Hệ thống:" & sysMsg)

        BroadcastScores()
        RefreshPlayerCards()
    End Sub

    Private Sub RefreshPlayerCards()
        Dim p As Integer
        For p = 0 To 3
            If pnlPlayers(p) Is Nothing Then Continue For

            Dim title As String = "Player " & (p + 1).ToString() & " — " & playerNames(p)
            If p = 0 Then title &= " (Host)"
            lblCardTitle(p).Text = title

            If Not playerConnected(p) Then
                lblCardStatus(p).Text = "(trống)"
                pnlPlayers(p).BackColor = Color.FromArgb(245, 245, 245)
            Else
                Dim scoreTxt As String = "Điểm: " & (If(scoresBySeat.ContainsKey(p), scoresBySeat(p), 0L)).ToString()
                Dim betTxt As String
                If perSeatFolded(p) Then
                    betTxt = "Đã bỏ bài (mất " & perSeatBetAmount(p).ToString() & " điểm)"
                ElseIf perSeatBetAmount(p) >= 0 AndAlso perSeatCardCount(p) > 0 Then
                    betTxt = "Đã cược " & perSeatBetAmount(p).ToString() & " điểm"
                    If state = RoundState.Betting AndAlso Not perSeatActedThisStreet(p) Then betTxt &= " — đang chờ đợt " & game.CurrentStreet.ToString() & "..."
                ElseIf state = RoundState.Betting AndAlso perSeatCardCount(p) > 0 Then
                    betTxt = "Đang chờ đặt cược đợt " & game.CurrentStreet.ToString() & "..."
                Else
                    betTxt = "Chưa vào ván này"
                End If
                lblCardStatus(p).Text = scoreTxt & "   |   " & betTxt
                pnlPlayers(p).BackColor = Color.White
            End If

            If pnlCardsRow(p) IsNot Nothing Then pnlCardsRow(p).Invalidate()
        Next p
    End Sub

    ' ============================================================
    '  CHAT PANEL
    ' ============================================================
    Private Sub BuildChatPanel(x As Integer, w As Integer, y As Integer, h As Integer)
        pnlChat = New Panel()
        pnlChat.Location = New Point(x, y)
        pnlChat.Size = New Size(w, h)

        lstChat = New ListBox()
        lstChat.Location = New Point(0, 0)
        lstChat.Size = New Size(w, h - 30)
        pnlChat.Controls.Add(lstChat)

        txtChatInput = New TextBox()
        txtChatInput.Location = New Point(0, h - 26)
        txtChatInput.Size = New Size(w - 55, 24)
        AddHandler txtChatInput.KeyDown, Sub(s As Object, ev As KeyEventArgs)
            If ev.KeyCode = Keys.Enter Then
                BtnSend_Click(s, EventArgs.Empty)
                ev.Handled = True
                ev.SuppressKeyPress = True
            End If
        End Sub
        pnlChat.Controls.Add(txtChatInput)

        btnSend = New Button()
        btnSend.Text = "Gửi"
        btnSend.Location = New Point(w - 50, h - 27)
        btnSend.Size = New Size(50, 26)
        AddHandler btnSend.Click, AddressOf BtnSend_Click
        pnlChat.Controls.Add(btnSend)

        pnlGame.Controls.Add(pnlChat)
    End Sub

    Private Sub BtnSend_Click(sender As Object, e As EventArgs)
        If txtChatInput.Text.Trim() = "" Then Return
        If localSeat < 0 Then Return
        Dim tag As String = "Player " & (localSeat + 1).ToString()
        Dim msg As String = txtChatInput.Text.Trim()
        AppendChat(tag & ": " & msg)

        If isHost Then
            hub.Broadcast("CHAT:" & tag & ":" & msg)
        Else
            If peer IsNot Nothing AndAlso peer.IsConnected Then peer.SendLine("CHAT:" & tag & ":" & msg)
        End If

        txtChatInput.Text = ""
        txtChatInput.Focus()
    End Sub

    Private Sub AppendChat(msg As String)
        If lstChat Is Nothing Then Return
        lstChat.Items.Add(msg)
        lstChat.TopIndex = Math.Max(0, lstChat.Items.Count - 1)
    End Sub

End Class
