
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reader;
using RFReaderConsole.DTO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Timers;
using WebSocketSharp;

namespace RFReaderConsole
{

    class Program
    {
        public static string _wsUserName = "";
        public static string _wsPassword = "";
        public static string LogStatus = "DEBUG2";
        public static bool AutoLoad = true;
        public static string _Api_Url;
        public static string _Robot_Id;
        public static string _Com_Port;
        public static string _Boud_Rate;
        public static string _Start_Read;
        public static string _Reading_Time_Elapsed;
        public static Reader.ReaderMethod reader;
        public static int DeviceAntennaCount = 0;
        public static string htxtSendData;
        public static string htxtCheckData;
        private static InventoryBuffer m_curInventoryBuffer = new InventoryBuffer();
        private static ReaderSetting m_curSetting = new ReaderSetting();
        private static int repeatCommand = 1;

        private static int m_nReceiveFlag = 0;
        private static bool m_bDisplayLog = false;
        private static bool m_bInventory = false;
        private static bool m_bLockTab = false;
        private static List<TagDto> _tagsList;
        public static int DeviceSerialPort = 0;
        private static int m_nTotal = 0;
        private static System.Timers.Timer timerInventory;
        private static System.Timers.Timer timerLocalization;
        private static WebSocket _ws;
        private static string _wslink;
        private static string _robotcode;
        private static string _distance;

        static void Main(string[] args)
        {
            if (LogStatus == "DEBUG")
            {
                args = new string[11];
                args[0] = "COM3";
                args[1] = "115200";
                args[2] = "Start";
                args[3] = "http://localhost:8000/robots/gettaglist";
                args[4] = "6";
                args[5] = "admin";
                args[6] = "HamzAsya";
                args[7] = "1000";
                args[8] = "localhost:8000";
                args[9] = "M02R201";
                args[10] = "100";
            }
            _Com_Port = args[0];
            _Boud_Rate = args[1];
            _Start_Read = args[2];
            _Api_Url = args[3];
            _Robot_Id = args[4];
            _wsUserName = args[5];
            _wsPassword = args[6];
            _Reading_Time_Elapsed = args[7];
            _wslink = "ws://" + args[8] + "/robots/iot/";
            _robotcode = args[9];
            _distance = args[10];

            GetTagList();
            OpenWebSocket();
            StartToRead();

            timerLocalization = new System.Timers.Timer();
            timerLocalization.Interval = 1000;
            timerLocalization.Elapsed += new ElapsedEventHandler(timerLocalization_Tick);
            timerLocalization.Enabled = true;

            if (Console.ReadKey().Key == ConsoleKey.Escape)
            {
                m_bInventory = true;
                m_curInventoryBuffer.bLoopInventory = true;
                reader.SignOut();
            }
            
        }

        private static void OpenWebSocket()
        {
            var link = _wslink + _robotcode + "/";
            _ws = new WebSocket(link);
            _ws.SetProxy(null, string.Empty, string.Empty);
            _ws.OnError += Ws_OnError;
            _ws.OnClose += Ws_OnClose;
            _ws.OnMessage += Ws_OnMessage;
            _ws.OnOpen += Ws_OnOpen;

            _ws.Connect();
        }

        private static void timerLocalization_Tick(object sender, ElapsedEventArgs e)
        {
            var time = Convert.ToInt32(_Reading_Time_Elapsed);
            var now = System.DateTime.Now;
            var taglist = _tagsList.Where(w => (now - w.fields.ReadingInfo.Max(m => m.ReadingTime)).Milliseconds <= time).ToList();

            foreach (var item in taglist)
            {
                var bestRSSI = 0;
                foreach (var reading in item.fields.ReadingInfo)
                {
                    if (reading.ReadingTime > now.AddMilliseconds(-1000) && reading.RSSI > bestRSSI)
                    {
                        bestRSSI = reading.RSSI;
                    }
                }
                item.fields.BestRSSI = bestRSSI;
            }
            var closesTags = taglist.OrderByDescending(o => o.fields.BestRSSI).Take(4).ToList();
            if (closesTags.Count >= 3)
            {

                var first = closesTags[1];
                var second = closesTags[2];
                var third = closesTags[3];

                var x1 = first.fields.PositionX - second.fields.PositionX;
                var y1 = first.fields.PositionY - second.fields.PositionY;

                var dist = Convert.ToInt32(_distance);
                x1 = first.fields.PositionX * dist + ((second.fields.BestRSSI / (first.fields.BestRSSI + second.fields.BestRSSI)) * (x1 * dist));
                y1 = first.fields.PositionY * dist + ((second.fields.BestRSSI / (first.fields.BestRSSI + second.fields.BestRSSI)) * (y1 * dist));


                var x2 = first.fields.PositionX - third.fields.PositionX;
                var y2 = first.fields.PositionY - third.fields.PositionY;

                x2 = first.fields.PositionX * dist + ((third.fields.BestRSSI / (first.fields.BestRSSI + third.fields.BestRSSI)) * (x2 * dist));
                y2 = first.fields.PositionY * dist + ((third.fields.BestRSSI / (first.fields.BestRSSI + third.fields.BestRSSI)) * (y2 * dist));


                var x3 = second.fields.PositionX - third.fields.PositionX;
                var y3 = second.fields.PositionY - third.fields.PositionY;

                x3 = second.fields.PositionX * dist + ((third.fields.BestRSSI / (second.fields.BestRSSI + third.fields.BestRSSI)) * (x3 * dist));
                y3 = second.fields.PositionY * dist + ((third.fields.BestRSSI / (second.fields.BestRSSI + third.fields.BestRSSI)) * (y3 * dist));
                var x0 = (x1 + x2 + x3) / 3;
                var y0 = (y1 + y2 + y3) / 3;

                string textKonum = "{\"message\":\"Son Konumum: x:" + x0 + "; y:" + y0 + "\"}";
                _ws.SendAsync(textKonum, delegate (bool completed3)
                {

                });




            }
        }


        private static void StartToRead()
        {
            try
            {
                timerInventory = new System.Timers.Timer();
                timerInventory.Interval = 500;
                timerInventory.Elapsed += new ElapsedEventHandler(timerInventory_Tick);

                DeviceAntennaCount = 4;
                reader = new Reader.ReaderMethod();
                DeviceSerialPort = 2;

                reader.AnalyCallback = AnalyData;
                Connect();

                new System.Threading.Thread(new System.Threading.ThreadStart(DetectPosition)).Start();

               

            }
            catch (Exception ex)
            {
                Logger(ex.Message);
            }
        }

        private static void DetectPosition()
        {

        }

        private static void GetTagList()
        {
            HttpWebRequest webrequest = (HttpWebRequest)WebRequest.Create(_Api_Url + "/" + _Robot_Id + "/");
            webrequest.Method = "GET";
            webrequest.ContentType = "application/x-www-form-urlencoded";
            webrequest.Headers.Add("Authorization", "Basic " + Convert.ToBase64String(Encoding.Default.GetBytes(_wsUserName + ":" + _wsPassword)));

            HttpWebResponse webresponse = (HttpWebResponse)webrequest.GetResponse();
            Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
            System.IO.StreamReader responseStream = new System.IO.StreamReader(webresponse.GetResponseStream(), enc);
            string result = string.Empty;
            result = responseStream.ReadToEnd();
            webresponse.Close();

            var data = JsonConvert.DeserializeObject<TagMasterDto>(result);
            _tagsList = JsonConvert.DeserializeObject<List<TagDto>>(data.tags);
        }

        private static void timerInventory_Tick(object sender, ElapsedEventArgs e)
        {
            m_nReceiveFlag++;
            if (m_nReceiveFlag >= 5)
            {
                RunLoopInventroy();
                m_nReceiveFlag = 0;
            }
        }

        private static void Connect()
        {
            string strException = string.Empty;
            string strComPort = _Com_Port;
            int nBaudrate = Convert.ToInt32(_Boud_Rate);

            int nRet = reader.OpenCom(strComPort, nBaudrate, out strException);
            if (nRet != 0)
            {
                string strLog = "Connection failed, failure cause: " + strException;
                Logger(strLog);

                throw new Exception(strLog);
            }
            else
            {
                #region Set Function Default
                htxtSendData = "A0 04 FF A0 00";
                CalculateCheckSumCommand(null, EventArgs.Empty);
                string SendCommandStr = htxtSendData + htxtCheckData;

                string[] reslut = CCommondMethod.StringToStringArray(SendCommandStr.ToUpper(), 2);
                byte[] btArySendData = CCommondMethod.StringArrayToByteArray(reslut, reslut.Length);

                reader.SendMessage(btArySendData);
                #endregion Set Function Default

                string strLog = "Connect" + strComPort + "@" + nBaudrate.ToString();
                Logger(strLog);
                if (_Start_Read == "Start")
                {
                    try
                    {
                        strLog = "Started to read tags";
                        Logger(strLog);
                        m_curInventoryBuffer.ClearInventoryPar();
                        m_curInventoryBuffer.btRepeat = Convert.ToByte(repeatCommand);

                        m_curInventoryBuffer.bLoopCustomizedSession = false;
                        m_curInventoryBuffer.lAntenna.Add(0x00);
                        m_curInventoryBuffer.lAntenna.Add(0x01);
                        m_curInventoryBuffer.lAntenna.Add(0x02);
                        m_curInventoryBuffer.lAntenna.Add(0x03);
                        if (m_curInventoryBuffer.lAntenna.Count == 0)
                        {
                            Logger("One antenna must be selected");
                        }

                        m_bInventory = true;
                        m_curInventoryBuffer.bLoopInventory = true;
                        m_curInventoryBuffer.bLoopInventoryReal = true;

                        m_nTotal = 0;

                        byte btWorkAntenna = m_curInventoryBuffer.lAntenna[m_curInventoryBuffer.nIndexAntenna];
                        reader.SetWorkAntenna(m_curSetting.btReadId, btWorkAntenna);
                        m_curSetting.btWorkAntenna = btWorkAntenna;


                        timerInventory.Enabled = true;
                        timerInventory.Enabled = true;
                    }
                    catch (Exception ex)
                    {
                        Logger(ex.Message);
                    }
                }
            }
        }

        private static void Disconnect()
        {
            try
            {
                reader.SignOut();
            }
            catch (Exception)
            {
            }
        }

        private static void CalculateCheckSumCommand(object sender, EventArgs e)
        {
            if (htxtSendData.Length == 0)
            {
                return;
            }

            string[] reslut = CCommondMethod.StringToStringArray(htxtSendData.ToUpper(), 2);
            byte[] btArySendData = CCommondMethod.StringArrayToByteArray(reslut, reslut.Length);

            byte btCheckData = reader.CheckValue(btArySendData);
            htxtCheckData = string.Format(" {0:X2}", btCheckData);
        }

        private static void AnalyData(MessageTran msgTran)
        {
            Logger("AnalyData started. " + msgTran.Cmd.ToString());
            m_nReceiveFlag = 0;
            if (msgTran.PacketType != 0xA0)
            {
                return;
            }
            switch (msgTran.Cmd)
            {
                //case 0x69:
                //    ProcessSetProfile(msgTran);
                //    break;
                //case 0x6A:
                //    ProcessGetProfile(msgTran);
                //    break;
                //case 0x71:
                //    ProcessSetUartBaudrate(msgTran);
                //    break;
                //case 0x72:
                //    ProcessGetFirmwareVersion(msgTran);
                //    break;
                //case 0x73:
                //    ProcessSetReadAddress(msgTran);
                //    break;
                case 0x74:
                    ProcessSetWorkAntenna(msgTran);
                    break;
                //case 0x75:
                //    ProcessGetWorkAntenna(msgTran);
                //    break;
                //case 0x76:
                //    ProcessSetOutputPower(msgTran);
                //    break;
                //case 0x77:
                //    ProcessGetOutputPower(msgTran);
                //    break;
                //case 0x78:
                //    ProcessSetFrequencyRegion(msgTran);
                //    break;
                //case 0x79:
                //    ProcessGetFrequencyRegion(msgTran);
                //    break;
                //case 0x7A:
                //    ProcessSetBeeperMode(msgTran);
                //    break;
                //case 0x7B:
                //    ProcessGetReaderTemperature(msgTran);
                //    break;
                //case 0x7C:
                //    ProcessSetDrmMode(msgTran);
                //    break;
                //case 0x7D:
                //    ProcessGetDrmMode(msgTran);
                //    break;
                //case 0x7E:
                //    ProcessGetImpedanceMatch(msgTran);
                //    break;
                //case 0x60:
                //    ProcessReadGpioValue(msgTran);
                //    break;
                //case 0x61:
                //    ProcessWriteGpioValue(msgTran);
                //    break;
                //case 0x62:
                //    ProcessSetAntDetector(msgTran);
                //    break;
                //case 0x63:
                //    ProcessGetAntDetector(msgTran);
                //    break;
                //case 0x67:
                //    ProcessSetReaderIdentifier(msgTran);
                //    break;
                //case 0x68:
                //    ProcessGetReaderIdentifier(msgTran);
                //    break;
                //case 0x80:
                //    ProcessInventory(msgTran);
                //    break;
                //case 0x81:
                //    ProcessReadTag(msgTran);
                //    break;
                //case 0x82:
                //    ProcessWriteTag(msgTran);
                //    break;
                //case 0x83:
                //    ProcessLockTag(msgTran);
                //    break;
                //case 0x84:
                //    ProcessKillTag(msgTran);
                //    break;
                //case 0x85:
                //    ProcessSetAccessEpcMatch(msgTran);
                //    break;
                //case 0x86:
                //    ProcessGetAccessEpcMatch(msgTran);
                //    break;
                case 0x89:
                case 0x8B:
                    ProcessInventoryReal(msgTran);
                    break;
                //case 0x8A:
                //    ProcessFastSwitch(msgTran);
                //    break;
                //case 0x8D:
                //    ProcessSetMonzaStatus(msgTran);
                //    break;
                //case 0x8E:
                //    ProcessGetMonzaStatus(msgTran);
                //    break;
                //case 0x90:
                //    ProcessGetInventoryBuffer(msgTran);
                //    break;
                //case 0x91:
                //    ProcessGetAndResetInventoryBuffer(msgTran);
                //    break;
                //case 0x92:
                //    ProcessGetInventoryBufferTagCount(msgTran);
                //    break;
                //case 0x93:
                //    ProcessResetInventoryBuffer(msgTran);
                //    break;
                //case 0xb0:
                //    ProcessInventoryISO18000(msgTran);
                //    break;
                //case 0xb1:
                //    ProcessReadTagISO18000(msgTran);
                //    break;
                //case 0xb2:
                //    ProcessWriteTagISO18000(msgTran);
                //    break;
                //case 0xb3:
                //    ProcessLockTagISO18000(msgTran);
                //    break;
                //case 0xb4:
                //    ProcessQueryISO18000(msgTran);
                //    break;
                default:
                    break;
            }
        }

        private static void ProcessSetWorkAntenna(MessageTran msgTran)
        {
            int intCurrentAnt = 0;
            intCurrentAnt = m_curSetting.btWorkAntenna + 1;
            string strCmd = "Set working antenna successfully, Current Ant: Ant" + intCurrentAnt.ToString();

            string strErrorCode = string.Empty;

            if (msgTran.AryData.Length == 1)
            {
                if (msgTran.AryData[0] == 0x10)
                {
                    m_curSetting.btReadId = msgTran.ReadId;
                    Logger(strCmd);

                    //Verify inventory operations
                    if (m_bInventory)
                    {
                        RunLoopInventroy();
                    }
                    return;
                }
                else
                {
                    strErrorCode = CCommondMethod.FormatErrorCode(msgTran.AryData[0]);
                }
            }
            else
            {
                strErrorCode = "Unknown Error";
            }

            string strLog = strCmd + "Failure, failure cause: " + strErrorCode;
            Logger(strLog);

            if (m_bInventory)
            {
                m_curInventoryBuffer.nCommond = 1;
                m_curInventoryBuffer.dtEndInventory = DateTime.Now;
                RunLoopInventroy();
            }
        }

        private delegate void RunLoopInventoryUnsafe();

        private static void RunLoopInventroy()
        {
            Logger("RunLoopInventroy() started");
            //Verify whether all antennas are completed inventory
            if (m_curInventoryBuffer.nIndexAntenna < m_curInventoryBuffer.lAntenna.Count - 1 || m_curInventoryBuffer.nCommond == 0)
            {
                if (m_curInventoryBuffer.nCommond == 0)
                {
                    m_curInventoryBuffer.nCommond = 1;

                    if (m_curInventoryBuffer.bLoopInventoryReal)
                    {
                        //m_bLockTab = true;

                        //btnInventory.Enabled = false;
                        //Logger("bLoopCustomizedSessionfalse ile dene. manuel atadık. silinebilir");
                        //m_curInventoryBuffer.bLoopCustomizedSession = false;

                        if (m_curInventoryBuffer.bLoopCustomizedSession)//User define Session and Inventoried Flag. 
                        {
                            Logger("User define Session and Inventoried Flag");
                            reader.CustomizedInventory(m_curSetting.btReadId, m_curInventoryBuffer.btSession, m_curInventoryBuffer.btTarget, m_curInventoryBuffer.btRepeat);
                        }
                        else //Inventory tags in real time mode
                        {
                            Logger("Inventory tags in real time mode");
                            reader.InventoryReal(m_curSetting.btReadId, m_curInventoryBuffer.btRepeat);

                        }
                    }
                    else
                    {
                        Logger("CODE: 476 ? ");
                        if (m_curInventoryBuffer.bLoopInventory)
                            reader.Inventory(m_curSetting.btReadId, m_curInventoryBuffer.btRepeat);
                    }
                }
                else
                {
                    m_curInventoryBuffer.nCommond = 0;
                    m_curInventoryBuffer.nIndexAntenna++;

                    byte btWorkAntenna = m_curInventoryBuffer.lAntenna[m_curInventoryBuffer.nIndexAntenna];
                    reader.SetWorkAntenna(m_curSetting.btReadId, btWorkAntenna);
                    m_curSetting.btWorkAntenna = btWorkAntenna;
                }
            }
            //Verify whether cycle inventory
            else if (m_curInventoryBuffer.bLoopInventory)
            {
                m_curInventoryBuffer.nIndexAntenna = 0;
                m_curInventoryBuffer.nCommond = 0;

                byte btWorkAntenna = m_curInventoryBuffer.lAntenna[m_curInventoryBuffer.nIndexAntenna];
                reader.SetWorkAntenna(m_curSetting.btReadId, btWorkAntenna);
                m_curSetting.btWorkAntenna = btWorkAntenna;
            }

        }

        private static void ProcessInventoryReal(MessageTran msgTran)
        {
            string strCmd = "";
            if (msgTran.Cmd == 0x89)
            {
                strCmd = "Real time inventory";
                Logger(strCmd);
            }
            if (msgTran.Cmd == 0x8B)
            {
                strCmd = "User define Session and Inventoried Flag inventory";
                Logger(strCmd);
            }
            string strErrorCode = string.Empty;

            Logger("Check This AryData.Length 1 OR 7 : " + msgTran.AryData.Length);

            if (msgTran.AryData.Length == 1)
            {
                strErrorCode = CCommondMethod.FormatErrorCode(msgTran.AryData[0]);
                string strLog = strCmd + "Failure, failure cause: " + strErrorCode;

                Logger(strLog);
                RefreshInventoryReal(0x00);
                RunLoopInventroy();
            }
            else if (msgTran.AryData.Length == 7)
            {
                m_curInventoryBuffer.nReadRate = Convert.ToInt32(msgTran.AryData[1]) * 256 + Convert.ToInt32(msgTran.AryData[2]);
                m_curInventoryBuffer.nDataCount = Convert.ToInt32(msgTran.AryData[3]) * 256 * 256 * 256 + Convert.ToInt32(msgTran.AryData[4]) * 256 * 256 + Convert.ToInt32(msgTran.AryData[5]) * 256 + Convert.ToInt32(msgTran.AryData[6]);

                Logger(strCmd);
                RefreshInventoryReal(0x01);
                RunLoopInventroy();
            }
            else
            {
                int nLength = msgTran.AryData.Length;
                int nEpcLength = nLength - 4;

                //Add inventory list
                //if (msgTran.AryData[3] == 0x00)
                //{
                //    MessageBox.Show("");
                //}
                string strEPC = CCommondMethod.ByteArrayToString(msgTran.AryData, 3, nEpcLength);
                string strPC = CCommondMethod.ByteArrayToString(msgTran.AryData, 1, 2);
                string strRSSI = (msgTran.AryData[nLength - 1] & 0x7F).ToString();
                SetMaxMinRSSI(Convert.ToInt32(msgTran.AryData[nLength - 1] & 0x7F));
                byte btTemp = msgTran.AryData[0];
                byte btAntId = (byte)((btTemp & 0x03) + 1);
                if ((msgTran.AryData[nLength - 1] & 0x80) != 0) btAntId += 4;
                m_curInventoryBuffer.nCurrentAnt = btAntId;
                string strAntId = btAntId.ToString();

                byte btFreq = (byte)(btTemp >> 2);
                string strFreq = GetFreqString(btFreq);

                //DataRow row = m_curInventoryBuffer.dtTagDetailTable.NewRow();
                //row[0] = strEPC;
                //row[1] = strRSSI;
                //row[2] = strAntId;
                //row[3] = strFreq;

                //m_curInventoryBuffer.dtTagDetailTable.Rows.Add(row);
                //m_curInventoryBuffer.dtTagDetailTable.AcceptChanges();

                ////Add tag list
                //DataRow[] drsDetail = m_curInventoryBuffer.dtTagDetailTable.Select(string.Format("COLEPC = '{0}'", strEPC));
                //int nDetailCount = drsDetail.Length;
                DataRow[] drs = m_curInventoryBuffer.dtTagTable.Select(string.Format("COLEPC = '{0}'", strEPC));
                if (drs.Length == 0)
                {
                    DataRow row1 = m_curInventoryBuffer.dtTagTable.NewRow();
                    row1[0] = strPC;
                    row1[2] = strEPC;
                    row1[4] = strRSSI;
                    row1[5] = "1";
                    row1[6] = strFreq;

                    m_curInventoryBuffer.dtTagTable.Rows.Add(row1);
                    m_curInventoryBuffer.dtTagTable.AcceptChanges();
                }
                else
                {
                    foreach (DataRow dr in drs)
                    {
                        dr.BeginEdit();

                        dr[4] = strRSSI;
                        dr[5] = (Convert.ToInt32(dr[5]) + 1).ToString();
                        dr[6] = strFreq;

                        dr.EndEdit();
                    }
                    m_curInventoryBuffer.dtTagTable.AcceptChanges();
                }

                m_curInventoryBuffer.dtEndInventory = DateTime.Now;
                RefreshInventoryReal(0x89);
            }
        }

        private delegate void RefreshInventoryRealUnsafe(byte btCmd);

        private static void RefreshInventoryReal(byte btCmd)
        {
            Logger("RefreshInventoryReal checkIF: 0x88  != :" + btCmd.ToString());
            switch (btCmd)
            {
                case 0x89:
                case 0x8B:
                    {
                        int nTagCount = m_curInventoryBuffer.dtTagTable.Rows.Count;
                        TimeSpan ts = m_curInventoryBuffer.dtEndInventory - m_curInventoryBuffer.dtStartInventory;
                        int nTotalTime = ts.Minutes * 60 * 1000 + ts.Seconds * 1000 + ts.Milliseconds;
                        int nCaculatedReadRate = 0;
                        int nCommandDuation = 0;

                        if (m_curInventoryBuffer.nReadRate == 0) //Software measure the speed before reader return speed.
                        {
                            if (nTotalTime > 0)
                            {
                                nCaculatedReadRate = 1;
                            }
                        }
                        else
                        {
                            nCommandDuation = m_curInventoryBuffer.nDataCount * 1000 / m_curInventoryBuffer.nReadRate;
                            nCaculatedReadRate = m_curInventoryBuffer.nReadRate;
                        }

                        //Variable of list

                        int nEpcLength = m_curInventoryBuffer.dtTagTable.Rows.Count;


                        //if (nEpcCount < nEpcLength)
                        //{
                        DataRow row = m_curInventoryBuffer.dtTagTable.Rows[nEpcLength - 1];

                        //string textToSend = row[2].ToString() + ":" + row[4].ToString();
                        //Logger(textToSend);

                        var tag = _tagsList.Where(w => w.fields.Code == row[2].ToString()).FirstOrDefault();
                        if (tag != null)
                        {
                            tag.fields.ReadingInfo.Add(new ReadingInfo
                            {
                                RSSI = Convert.ToInt32(row[4]),
                                ReadingTime = System.DateTime.Now,
                            });
                        }

                        //RFGateSocket rfocket = new RFGateSocket(new IPEndPoint(IPAddress.Parse(SERVER_IP).Address, PORT_NO));
                        //rfocket.Start();

                        //rfocket.SendData(textToSend);


                        //TcpClient client = new TcpClient(SERVER_IP, PORT_NO);
                        //NetworkStream nwStream = client.GetStream();
                        //byte[] bytesToSend = ASCIIEncoding.ASCII.GetBytes(textToSend);

                        //nwStream.Write(bytesToSend, 0, bytesToSend.Length);

                        //client.Close();
                        //}

                        //else
                        //{
                        //    int nIndex = 0;
                        //    foreach (DataRow row in m_curInventoryBuffer.dtTagTable.Rows)
                        //    {
                        //        ListViewItem item = ltvInventoryEpc.Items[nIndex];
                        //        item.SubItems[3].Text = row[5].ToString();
                        //        nIndex++;
                        //    }
                        //}

                        //Update the number of read time in list.
                        //if (m_nTotal % m_nRealRate == 1)
                        //{
                        //int nIndex = 0;
                        //foreach (DataRow row in m_curInventoryBuffer.dtTagTable.Rows)
                        //{

                        //    if (row[2].ToString().Trim() == lblSolUst.Text)
                        //    {
                        //        lblSolUstDeger.Text = row[4].ToString();
                        //    }
                        //    else if (row[2].ToString().Trim() == lblSagUst.Text)
                        //    {
                        //        lblSagUstDeger.Text = row[4].ToString();
                        //    }
                        //    else if (row[2].ToString().Trim() == lblSolAlt.Text)
                        //    {
                        //        lblSolAltDeger.Text = row[4].ToString();
                        //    }

                        //    else if (row[2].ToString().Trim() == lblSagAlt.Text)
                        //    {
                        //        lblSagAltDeger.Text = row[4].ToString();
                        //    }
                        //    //ListViewItem item;
                        //    //item.SubItems[3].Text = row[5].ToString();
                        //    //item.SubItems[4].Text = (Convert.ToInt32(row[4]) - 129).ToString() + "dBm";
                        //    //item.SubItems[5].Text = row[6].ToString();
                        //    row[2].ToString();
                        //    nIndex++;
                        //}

                        //if (lblSolAltDeger.Text != "0" && lblSagAltDeger.Text != "0" && lblSolUstDeger.Text != "0" && lblSagUstDeger.Text != "0")
                        //{
                        //    double solUst = Convert.ToDouble(lblSolUstDeger.Text);
                        //    double sagUst = Convert.ToDouble(lblSagUstDeger.Text);
                        //    double solAlt = Convert.ToDouble(lblSolAltDeger.Text);
                        //    double sagAlt = Convert.ToDouble(lblSagAltDeger.Text);

                        //    double panelLeft = Convert.ToDouble(panel2.Left);
                        //    double panelRight = Convert.ToDouble(panel2.Right);
                        //    double panelTop = Convert.ToDouble(panel2.Top);
                        //    double panelBottom = Convert.ToDouble(panel2.Bottom);

                        //    int left = Convert.ToInt32(sagUst * ((panelRight - panelLeft) / (solUst + sagUst)));

                        //    int top = Convert.ToInt32(solAlt * ((panelBottom - panelTop) / (sagUst + sagAlt)));
                        //    pRobot.Left = left;
                        //    pRobot.Top = top;
                        //    pRobot.Visible = true;
                        //}
                        //}

                        //if (ltvInventoryEpc.SelectedIndices.Count != 0)
                        //{
                        //    int nDetailCount = ltvInventoryTag.Items.Count;
                        //    int nDetailLength = m_curInventoryBuffer.dtTagDetailTable.Rows.Count;

                        //    foreach (int nIndex in ltvInventoryEpc.SelectedIndices)
                        //    {
                        //        ListViewItem itemEpc = ltvInventoryEpc.Items[nIndex];
                        //        DataRow row = m_curInventoryBuffer.dtTagDetailTable.Rows[nDetailLength - 1];
                        //        if (itemEpc.SubItems[1].Text == row[0].ToString())
                        //        {
                        //            ListViewItem item = new ListViewItem();
                        //            item.Text = (nDetailCount + 1).ToString();
                        //            item.SubItems.Add(row[0].ToString());

                        //            string strTemp = (Convert.ToInt32(row[1].ToString()) - 129).ToString() + "dBm";
                        //            item.SubItems.Add(strTemp);
                        //            byte byTemp = Convert.ToByte(row[1]);
                        //            if (byTemp > 0x50)
                        //            {
                        //                item.BackColor = Color.PowderBlue;
                        //            }
                        //            else if (byTemp < 0x30)
                        //            {
                        //                item.BackColor = Color.LemonChiffon;
                        //            }

                        //            item.SubItems.Add(row[2].ToString());
                        //            item.SubItems.Add(row[3].ToString());

                        //            ltvInventoryTag.Items.Add(item);
                        //            ltvInventoryTag.Items[nDetailCount].EnsureVisible();
                        //        }
                        //    }
                        //}
                        //else
                        //{
                        //    int nDetailCount = ltvInventoryTag.Items.Count;
                        //    int nDetailLength = m_curInventoryBuffer.dtTagDetailTable.Rows.Count;

                        //    DataRow row = m_curInventoryBuffer.dtTagDetailTable.Rows[nDetailLength - 1];
                        //    ListViewItem item = new ListViewItem();
                        //    item.Text = (nDetailCount + 1).ToString();
                        //    item.SubItems.Add(row[0].ToString());

                        //    string strTemp = (Convert.ToInt32(row[1].ToString()) - 129).ToString() + "dBm";
                        //    item.SubItems.Add(strTemp);
                        //    byte byTemp = Convert.ToByte(row[1]);
                        //    if (byTemp > 0x50)
                        //    {
                        //        item.BackColor = Color.PowderBlue;
                        //    }
                        //    else if (byTemp < 0x30)
                        //    {
                        //        item.BackColor = Color.LemonChiffon;
                        //    }

                        //    item.SubItems.Add(row[2].ToString());
                        //    item.SubItems.Add(row[3].ToString());

                        //    ltvInventoryTag.Items.Add(item);
                        //    ltvInventoryTag.Items[nDetailCount].EnsureVisible();
                        //}


                    }
                    break;


                case 0x00:
                case 0x01:
                    {
                        m_bLockTab = false;


                    }
                    break;
                default:
                    break;
            }

        }

        private static void SetMaxMinRSSI(int nRSSI)
        {
            if (m_curInventoryBuffer.nMaxRSSI < nRSSI)
            {
                m_curInventoryBuffer.nMaxRSSI = nRSSI;
            }

            if (m_curInventoryBuffer.nMinRSSI == 0)
            {
                m_curInventoryBuffer.nMinRSSI = nRSSI;
            }
            else if (m_curInventoryBuffer.nMinRSSI > nRSSI)
            {
                m_curInventoryBuffer.nMinRSSI = nRSSI;
            }
        }

        private static string GetFreqString(byte btFreq)
        {
            string strFreq = string.Empty;

            if (m_curSetting.btRegion == 4)
            {
                float nExtraFrequency = btFreq * m_curSetting.btUserDefineFrequencyInterval * 10;
                float nstartFrequency = ((float)m_curSetting.nUserDefineStartFrequency) / 1000;
                float nStart = nstartFrequency + nExtraFrequency / 1000;
                string strTemp = nStart.ToString("0.000");
                return strTemp;
            }
            else
            {
                if (btFreq < 0x07)
                {
                    float nStart = 865.00f + Convert.ToInt32(btFreq) * 0.5f;

                    string strTemp = nStart.ToString("0.00");

                    return strTemp;
                }
                else
                {
                    float nStart = 902.00f + (Convert.ToInt32(btFreq) - 7) * 0.5f;

                    string strTemp = nStart.ToString("0.00");

                    return strTemp;
                }
            }
        }

        private static void Logger(string input)
        {
            if (LogStatus == "DEBUG")
            {
                Console.WriteLine(input + " -- " + DateTime.Now.ToLongTimeString());
            }
        }

        private static void Ws_OnError(object sender, ErrorEventArgs e)
        {
            Logger(e.Message);
            CloseWebSocket((WebSocket)sender);
        }

        private static void CloseWebSocket(WebSocket ws)
        {
            if (ws != null && ws.IsAlive)
            {
                ws.OnClose -= Ws_OnClose;
                ws.Close();
                ws = null;
            }
        }

        private static void Ws_OnClose(object sender, CloseEventArgs e)
        {
            Logger(e.Reason);
            CloseWebSocket((WebSocket)sender);
        }

        private static void Ws_OnMessage(object sender, MessageEventArgs e)
        {

            if (e.Data == "{\"message\": \"Orada misin?\"}")
            {
                string text = "{\"message\":\"Evet\"}";
                ((WebSocket)sender).SendAsync(text, delegate (bool completed) { });
            }
            //TODO: Mesaj geldiğinde ve göndedrildiğinde burası çalışacak
        }
        private static void Ws_OnOpen(object sender, EventArgs e)
        {
            // Bağlantı oluştuğunda yapılacaklar
        }

    }

    public class WebSocketRequest
    {
        public WebSocket _ws { get; set; }
        public string message { get; set; }
    }
}
