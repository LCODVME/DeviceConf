using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO.Ports;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace wc_devconf
{
    public partial class Form1 : Form
    {
        const int CONNECT_MODE_IP = 0;
        const int CONNECT_MODE_COM = 1;
        const int DEVICE_GATW = 1;
        const int DEVICE_BASE = 2;
        const int DISCONNECT = 0;
        const int CONNECTING = 1;
        const int CONNECTED = 2;
        const int TRANSMIT_HEAD = 0xABCD;
        const int CONNECT_REQUEST_CODE = 3000;
        const int CONNECT_RESOPNCE_CODE = 4000;
        const int READ_CONFIG_REQUEST_CODE = 3001;
        const int READ_CONFIG_RESPONCE_CODE = 4001;
        const int WRITE_CONFIG_REQUEST_CODE = 3002;
        const int WRITE_CONFIG_RESPONCE_CODE = 4002;
        const int DISCONNECT_CODE = 3003;
        const int REBOOT_CODE = 3004;
        const int HEART_BEAT_CODE = 3005;
        const int HEART_BEAT_RESPONCE = 4005;
        const int TRANS_DATA_CODE = 5050;

        [StructLayoutAttribute(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
        public struct TransmitFormat_t
        {
            public int head;
            public int code;
            public int datalen;
            public byte[] data;
        };

        public struct ConfigFormat_t
        {
            public int channel;
            public int power;
            public int speed;
        };

        static int ConMode = CONNECT_MODE_COM;
        static int Constate = DISCONNECT;
        static int typedev = DEVICE_GATW;
        IPEndPoint remotePoint;
        SerialPort ComDevice;
        UdpClient udp_client;
        Thread udpRcvThread;
        static int heartBeatCnt = 0;
        static int retranCount = 0;
        static int cntTimeCount = 0;
        System.Timers.Timer heartBeat = new System.Timers.Timer(3000);
        System.Timers.Timer retransmit = new System.Timers.Timer(1000);
        System.Timers.Timer cnttimer = new System.Timers.Timer(1000);
        public delegate void delegateCall();
        delegateCall msgRetransmit;
        delegateCall msgRetransmit_old;

        int[,] lora_rate = new int[6, 3] { { 7, 12, 1 },
                                           { 8, 7, 1 },
                                           { 9, 11, 1 },
                                           { 8, 8, 2 },
                                           { 9, 8, 2 },
                                           { 9, 7, 1 } };

        public Form1()
        {
            InitializeComponent();
            Control.CheckForIllegalCrossThreadCalls = false;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if(ConMode == CONNECT_MODE_COM)
            {
                radioButton2.Checked = true;
            }
            radioButton3.Checked = true;
            comboBox4.Text = "8";
            comboBox5.Text = "115200";
            comboBox7.Text = "1";
            comboBox8.Text = "无";
            comboBox9.Text = "15";
            comboBox10.Text = "5";
            comboBox11.Text = "1";
            /* create retransmit timer */
            retransmit.Elapsed += new System.Timers.ElapsedEventHandler(retransmitTime);
            retransmit.AutoReset = true;
            retransmit.Enabled = true;
            retransmit.Stop();
            /* create heart beat timer */
            heartBeat.Elapsed += new System.Timers.ElapsedEventHandler(heartBeatTimer);
            heartBeat.AutoReset = true;
            heartBeat.Enabled = true;
            heartBeat.Stop();
            /* create sys timer */
            cnttimer.Elapsed += new System.Timers.ElapsedEventHandler(cntTimerPro);
            cnttimer.AutoReset = true;
            cnttimer.Enabled = true;
            cnttimer.Stop();
        }

        private void printMessage(string msg, string forecolor)
        {           
            int temp = richTextBox1.Text.Length;
            richTextBox1.AppendText(msg);
            //richTextBox1.SelectionStart = temp;
            //richTextBox1.SelectionLength = richTextBox1.Text.Length - temp;
            richTextBox1.Select(temp, richTextBox1.Text.Length - temp);
            switch (forecolor)
            {
                case "red":
                    {
                        richTextBox1.SelectionColor = Color.Red;
                        break;
                    }
                case "green":
                    {
                        richTextBox1.SelectionColor = Color.Green;
                        break;
                    }
                case "blue":
                    {
                        richTextBox1.SelectionColor = Color.Blue;
                        break;
                    }
                case "yellow":
                    {
                        richTextBox1.SelectionColor = Color.Yellow;
                        break;
                    }
                case "olive":
                    {
                        richTextBox1.SelectionColor = Color.Olive;
                        break;
                    }
                default:
                    {
                        richTextBox1.SelectionColor = Color.Green;
                        break;
                    }
            }
            //richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.Select(richTextBox1.Text.Length, 0);
            richTextBox1.ScrollToCaret();
        }

        private void transmitFaildCallback()
        {
            if(Constate == CONNECTING)
            {
                Constate = DISCONNECT;
                button1.Text = "连接";
                label4.Text = "未连接";
                radioButton1.Enabled = true;
                radioButton2.Enabled = true;
                radioButton3.Enabled = true;
                radioButton4.Enabled = true;
                printMessage("连接超时!\r\n", "red");
                if (ConMode == CONNECT_MODE_COM)
                {
                    comboBox4.Enabled = true;
                    comboBox5.Enabled = true;
                    comboBox6.Enabled = true;
                    comboBox7.Enabled = true;
                    comboBox8.Enabled = true;
                    ComDevice.Close();
                }
                else
                {
                    textBox1.ReadOnly = false;
                    textBox2.ReadOnly = false;
                    textBox3.ReadOnly = false;
                    textBox4.ReadOnly = false;
                    textBox26.ReadOnly = false;
                }
            }
        }

        private void retransmitTime(object source, System.Timers.ElapsedEventArgs e)
        {
            if (msgRetransmit != null)
            {
                msgRetransmit();
            }
            else
            {
                retransmit.Stop();
                retranCount = 0;
            }
            if (msgRetransmit_old == msgRetransmit && ++retranCount > 3)
            {
                msgRetransmit_old = null;
                msgRetransmit = null;
                transmitFaildCallback();
                printMessage("操作失败！\r\n", "red");
            }
            else if (msgRetransmit_old != msgRetransmit)
            {
                msgRetransmit_old = msgRetransmit;
                retranCount = 0;
            }
        }

        private void heartBeatTimer(object source, System.Timers.ElapsedEventArgs e)
        {
            heartBeatSend();

            if (++heartBeatCnt > 2)
            {
                heartBeat.Stop();
                Constate = DISCONNECT;
                button1.Text = "连接";
                label4.Text = "未连接";
                if (ConMode == CONNECT_MODE_COM)
                {
                    comboBox4.Enabled = true;
                    comboBox5.Enabled = true;
                    comboBox6.Enabled = true;
                    comboBox7.Enabled = true;
                    comboBox8.Enabled = true;
                    ComDevice.Close();
                }
                else
                {
                    textBox1.ReadOnly = false;
                    textBox2.ReadOnly = false;
                    textBox3.ReadOnly = false;
                    textBox4.ReadOnly = false;
                    textBox26.ReadOnly = false;
                }
                printMessage("连接断开！\r\n", "red");
                radioButton1.Enabled = true;
                radioButton2.Enabled = true;
                radioButton3.Enabled = true;
                radioButton4.Enabled = true;
                cntTimeCount = 0;
            }
        }

        private void cntTimerPro(object source, System.Timers.ElapsedEventArgs e)
        {
            if(--cntTimeCount <= 0)
            {
                cnttimer.Stop();
                label19.Text = "";
                printMessage("停止.\r\n", "blue");
            }
            else
            {
                label19.Text = cntTimeCount.ToString() + "s";
            }
        }

        private void transmitSendData(byte[] data, int datalen)
        {
            if (ConMode == CONNECT_MODE_COM)
            {
                try
                {
                    ComDevice.Write(data, 0, datalen);
                }
                catch
                {
                    printMessage("串口断开。\r\n", "red");
                    ComDevice.Close();
                }
            }
            else
            {
                udp_client.Send(data, datalen, remotePoint);
            }
        }

        private static byte[] StructToBytes(object structObj)
        {
            //得到结构体的大小
            int size = Marshal.SizeOf(structObj);
            //创建byte数组
            byte[] bytes = new byte[size];
            //分配结构体大小的内存空间
            IntPtr structPtr = Marshal.AllocHGlobal(size);
            //将结构体拷到分配好的内存空间
            Marshal.StructureToPtr(structObj, structPtr, false);
            //从内存空间拷到byte数组
            Marshal.Copy(structPtr, bytes, 0, size);
            //释放内存空间
            Marshal.FreeHGlobal(structPtr);
            //返回byte数组
            return bytes;
        }

        private static Object BytesToStruct(Byte[] bytes, Type strcutType)
        {
            Int32 size = Marshal.SizeOf(strcutType);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.Copy(bytes, 0, buffer, size);

                return Marshal.PtrToStructure(buffer, strcutType);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private void connectDevice( )
        {
            TransmitFormat_t connectdata = new TransmitFormat_t();
            connectdata.head = TRANSMIT_HEAD;
            connectdata.code = CONNECT_REQUEST_CODE;
            connectdata.datalen = 0;
            transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
            msgRetransmit = new delegateCall(connectDevice);
            retransmit.Start();
        }

        private void heartBeatSend()
        {
            TransmitFormat_t connectdata = new TransmitFormat_t();
            connectdata.head = TRANSMIT_HEAD;
            connectdata.code = HEART_BEAT_CODE;
            connectdata.datalen = 0;
            transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
        }

        private void readConfigRequest()
        {
            TransmitFormat_t connectdata = new TransmitFormat_t();
            connectdata.head = TRANSMIT_HEAD;
            connectdata.code = READ_CONFIG_REQUEST_CODE;
            connectdata.datalen = 0;
            transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
            msgRetransmit = new delegateCall(readConfigRequest);
            retransmit.Start();
        }

        private void writeConfigRequest( )
        {
            try
            {
                TransmitFormat_t connectdata = new TransmitFormat_t();
                ConfigFormat_t configData = new ConfigFormat_t();
                configData.channel = int.Parse(comboBox1.Text);
                configData.power = int.Parse(comboBox3.Text);
                configData.speed = int.Parse(comboBox2.Text);
                connectdata.head = TRANSMIT_HEAD;
                connectdata.code = WRITE_CONFIG_REQUEST_CODE;
                connectdata.datalen = Marshal.SizeOf(configData);
                connectdata.data = StructToBytes(configData);
                transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
                msgRetransmit = new delegateCall(writeConfigRequest);
                retransmit.Start();
            }
            catch
            {
                MessageBox.Show("配置填写错误，请检查！");
            }
        }

        private void rebootDevice()
        {
            TransmitFormat_t connectdata = new TransmitFormat_t();
            connectdata.head = TRANSMIT_HEAD;
            connectdata.code = REBOOT_CODE;
            connectdata.datalen = 0;
            transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
            msgRetransmit = new delegateCall(rebootDevice);
            retransmit.Start();
        }

        private void disconnect()
        {
            TransmitFormat_t connectdata = new TransmitFormat_t();
            connectdata.head = TRANSMIT_HEAD;
            connectdata.code = DISCONNECT_CODE;
            connectdata.datalen = 0;
            transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
        }

        /*protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0219)
            {//设备被拔出
                if (m.WParam.ToInt32() == 0x8004)//usb串口
                {
                    
                    //if (对串口进行操作)
                    //{//产生异常
                    //      关闭串口
                    //}
                    MessageBox.Show("USB转串口拔出");

                }
            }
            base.WndProc(ref m);
        }*/

        private void button5_Click(object sender, EventArgs e)
        {
            richTextBox1.Clear();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if(Constate == DISCONNECT)
            {
                if(ConMode == CONNECT_MODE_COM)
                {
                    comboBox4.Enabled = false;
                    comboBox5.Enabled = false;
                    comboBox6.Enabled = false;
                    comboBox7.Enabled = false;
                    comboBox8.Enabled = false;
                    printMessage("connect device by serial.\r\n", "blue");
                    ComDevice = new SerialPort();
                    ComDevice.PortName = comboBox6.Text;
                    ComDevice.BaudRate = Convert.ToInt32(comboBox5.Text);
                    ComDevice.DataBits = Convert.ToInt32(comboBox4.Text);
                    ComDevice.StopBits = (StopBits)Convert.ToInt32(comboBox7.Text);
                    ComDevice.DataReceived += new SerialDataReceivedEventHandler(serialReceivePro);
                    switch (comboBox8.Text)
                    {
                        case "无": ComDevice.Parity = Parity.None;break;
                        case "奇校验": ComDevice.Parity = Parity.Odd; break;
                        case "偶校验": ComDevice.Parity = Parity.Even; break;
                        default: ComDevice.Parity = Parity.None; break;
                    }
                    try
                    {
                        ComDevice.Open();
                    }
                    catch
                    {
                        comboBox4.Enabled = true;
                        comboBox5.Enabled = true;
                        comboBox6.Enabled = true;
                        comboBox7.Enabled = true;
                        comboBox8.Enabled = true;
                        printMessage("打开串口失败!\r\n", "red");
                        MessageBox.Show("打开串口失败!");
                        ComDevice.Close();
                        return;
                    }
                }
                else
                {
                    printMessage("connect device by ip net.\r\n", "blue");
                    try
                    {
                        textBox1.ReadOnly = true;
                        textBox2.ReadOnly = true;
                        textBox3.ReadOnly = true;
                        textBox4.ReadOnly = true;
                        textBox26.ReadOnly = true;
                        string dstIP = String.Format("{0}.{1}.{2}.{3}", textBox1.Text, textBox2.Text, textBox3.Text, textBox4.Text);
                        int remotePort = int.Parse(textBox26.Text);
                        IPAddress remoteIP = IPAddress.Parse(dstIP);
                        remotePoint = new IPEndPoint(remoteIP, remotePort);
                        udp_client = new UdpClient();
                        udpRcvThread = new Thread(udpReceivePro);
                        udpRcvThread.Start();
                    }
                    catch
                    {
                        textBox1.ReadOnly = false;
                        textBox2.ReadOnly = false;
                        textBox3.ReadOnly = false;
                        textBox4.ReadOnly = false;
                        textBox26.ReadOnly = false;
                        textBox1.Focus();
                        printMessage("地址无效，请输入正确的IP地址！\r\n", "red");
                        MessageBox.Show("地址无效，请输入正确的IP地址！");
                        return;
                    }
                }
                radioButton1.Enabled = false;
                radioButton2.Enabled = false;
                radioButton3.Enabled = false;
                radioButton4.Enabled = false;
                Constate = CONNECTING;
                label4.Text = "正在连接";
                button1.Text = "取消";
                printMessage("连接中,请稍等。。。\r\n", "green");
                if (typedev == DEVICE_BASE)
                {
                    Constate = CONNECTED;
                    printMessage("连接成功!\r\n", "green");
                    button1.Text = "断开连接";
                    label4.Text = "已连接";
                }
                else
                {
                    connectDevice();
                }
            }
            else
            {
                button1.Text = "连接";
                label4.Text = "未连接";
                msgRetransmit = null;
                if (Constate == CONNECTED && 
                    typedev != DEVICE_BASE) disconnect();
                if (ConMode == CONNECT_MODE_COM)
                {
                    comboBox4.Enabled = true;
                    comboBox5.Enabled = true;
                    comboBox6.Enabled = true;
                    comboBox7.Enabled = true;
                    comboBox8.Enabled = true;
                    ComDevice.Close();
                }
                else
                {
                    textBox1.ReadOnly = false;
                    textBox2.ReadOnly = false;
                    textBox3.ReadOnly = false;
                    textBox4.ReadOnly = false;
                    textBox26.ReadOnly = false;
                    udpRcvThread.Abort();
                }
                printMessage("连接关闭.\r\n", "green");
                Constate = DISCONNECT;
                cntTimeCount = 0;
                radioButton1.Enabled = true;
                radioButton2.Enabled = true;
                radioButton3.Enabled = true;
                radioButton4.Enabled = true;
            }
        }

        private void serialReceivePro(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                byte[] bytRcv = new byte[ComDevice.BytesToRead];
                ComDevice.Read(bytRcv, 0, bytRcv.Length);
                if (bytRcv.Length > 0)
                {
                    recvDataDecode(bytRcv);
                }
            }
            catch { }
        }

        private void udpReceivePro(object obj)
        {
            IPEndPoint localPoint = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    byte[] bytRcv = udp_client.Receive(ref localPoint);
                    if (bytRcv.Length > 0)
                    {
                        recvDataDecode(bytRcv);
                    }
                }
                catch { }
            }

        }

        private void recvDataDecode(byte[] data)
        {
            try
            {
                TransmitFormat_t recvData = (TransmitFormat_t)BytesToStruct(data, typeof(TransmitFormat_t));
                //if (recvData.head != TRANSMIT_HEAD) return;
                switch (recvData.code)
                {
                    case CONNECT_RESOPNCE_CODE:
                        {
                            msgRetransmit = null;
                            heartBeatCnt = 0;
                            printMessage("连接成功!\r\n", "green");
                            button1.Text = "断开连接";
                            label4.Text = "已连接";
                            heartBeat.Start();
                            Constate = CONNECTED;
                            break;
                        }
                    case READ_CONFIG_RESPONCE_CODE:
                        {
                            msgRetransmit = null;
                            heartBeatCnt = 0;
                            ConfigFormat_t configData = (ConfigFormat_t)BytesToStruct(recvData.data, typeof(ConfigFormat_t));
                            comboBox1.Text = configData.channel.ToString();
                            comboBox3.Text = configData.power.ToString();
                            comboBox2.Text = configData.speed.ToString();
                            printMessage("配置读取成功!\r\n", "green");
                            break;
                        }
                    case WRITE_CONFIG_RESPONCE_CODE:
                        {
                            msgRetransmit = null;
                            heartBeatCnt = 0;
                            printMessage("配置写入成功!\r\n", "green");
                            break;
                        }
                    case HEART_BEAT_RESPONCE:
                        heartBeatCnt = 0;
                        break;
                    case TRANS_DATA_CODE:
                        {
                            heartBeatCnt = 0;
                            break;
                        }
                    default:
                        //printMessage(System.Text.Encoding.Default.GetString(data) + "\r\n", "olive");
                        printMessage(BitConverter.ToString(data, 0).Replace(" ", string.Empty).ToLower() + "\r\n", "olive");
                        break;
                }
            }
            catch { printMessage(BitConverter.ToString(data, 0).Replace(" ", string.Empty).ToLower() + "\r\n", "olive"); }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            if(Constate == CONNECTED)
            {
                printMessage("读取配置.\r\n", "blue");
                readConfigRequest();
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                printMessage("写入配置.\r\n", "blue");
                writeConfigRequest();
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button6_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                printMessage("发送传感器配置.\r\n", "blue");
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                printMessage("重启设备.\r\n", "green");
                rebootDevice();
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                if (typedev != DEVICE_BASE)
                {
                    printMessage("发送轮询指令.\r\n", "blue");
                    TransmitFormat_t connectdata = new TransmitFormat_t();
                    connectdata.head = TRANSMIT_HEAD;
                    connectdata.code = TRANS_DATA_CODE;
                    connectdata.datalen = 0;
                    connectdata.data = new byte[10] { byte.Parse(textBox5.Text), 0x02, 0x00, 0x01, byte.Parse(textBox5.Text), 0x01, 0x00, 0x01, 0x00, 0x00 };
                    //printMessage(BitConverter.ToString(connectdata.data, 0).Replace(" ", string.Empty).ToLower() + "\r\n", "olive");
                    transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
                }
                else
                {
                    byte[] pollcmd = new byte[10] { byte.Parse(textBox5.Text), 0x02, 0x00, 0x01, byte.Parse(textBox5.Text), 0x01, 0x00, 0x01, 0x00, 0x00 };
                    transmitSendData(pollcmd, pollcmd.Length);
                }
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button9_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                if (typedev != DEVICE_BASE)
                {
                    printMessage("发送睡眠指令.\r\n", "blue");
                    TransmitFormat_t connectdata = new TransmitFormat_t();
                    connectdata.head = TRANSMIT_HEAD;
                    connectdata.code = TRANS_DATA_CODE;
                    connectdata.datalen = 0;
                    connectdata.data = new byte[10] { 0xfd, 0x02, 0x00, 0x01, byte.Parse(textBox5.Text), 0x01, 0x00, 0x01, 0x00, 0x00 };
                    transmitSendData(StructToBytes(connectdata), Marshal.SizeOf(connectdata));
                }
                else
                {
                    byte[] pollcmd = new byte[10] { 0xfd, 0x02, 0x00, 0x01, byte.Parse(textBox5.Text), 0x01, 0x00, 0x01, 0x00, 0x00 };
                    transmitSendData(pollcmd, pollcmd.Length);
                }
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            Environment.Exit(0);
        }

        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textboxName = (TextBox)sender;
            char[] nameArray = this.ActiveControl.Name.ToCharArray();
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
            string n = new string(nameArray);
            TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

            if (e.KeyCode == Keys.Right && textboxName.SelectionStart == textboxName.Text.Length)
            {
                textboxNextName.Focus();
            }
            else if (e.KeyCode == Keys.Enter && nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] < '4')
            {
                button1_Click(sender, e);
            }
        }

        private void textBox1_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                object o = this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                TextBox textboxName = (TextBox)o;
                char[] nameArray = this.ActiveControl.Name.ToCharArray();
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
                string n = new string(nameArray);
                TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

                if (e.KeyChar != '\b')
                {
                    if (e.KeyChar < '0' || e.KeyChar > '9')
                    {
                        e.Handled = true;
                        if (e.KeyChar == '.' && textboxName.Text.Length > 0)
                        {
                            textboxNextName.Focus();
                        }
                    }
                    if (textboxName.Text.Length >= 2)
                    {
                        textboxNextName.Focus();
                    }
                }
            }
            catch { }
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            TextBox textboxName = (TextBox)sender;
            try
            {
                if (int.Parse(textboxName.Text) > 255)
                {
                    MessageBox.Show("IP地址错误，请重新输入！");
                    textboxName.Text = "";
                }
            }
            catch
            {
                textboxName.Focus();
            }
        }

        private void textBox2_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textboxName = (TextBox)this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
            char[] nameArray = this.ActiveControl.Name.ToCharArray();
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
            string n = new string(nameArray);
            TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 2);
            n = new string(nameArray);
            TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

            if (e.KeyCode == Keys.Right && textboxName.SelectionStart == textboxName.Text.Length)
            {
                textboxNextName.Focus();
            }
            else if (e.KeyCode == Keys.Left && textboxName.SelectionStart == 0)
            {
                textboxLastName.Focus();
            }
            else if (e.KeyCode == Keys.Enter && nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] < '4')
            {
                button1_Click(sender, e);
            }
        }

        private void textBox2_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                TextBox textboxName = (TextBox)this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                char[] nameArray = this.ActiveControl.Name.ToCharArray();
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
                string n = new string(nameArray);
                TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 2);
                n = new string(nameArray);
                TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

                if (e.KeyChar != '\b')
                {
                    if (e.KeyChar < '0' || e.KeyChar > '9')
                    {
                        e.Handled = true;
                        if (e.KeyChar == '.' && textboxName.Text.Length > 0)
                        {
                            textboxNextName.Focus();
                        }
                    }
                    if (textboxName.Text.Length >= 2)
                    {
                        textboxNextName.Focus();
                    }
                }
                else
                {
                    if (textboxName.SelectionStart == 0)
                    {
                        textboxLastName.Focus();
                    }
                }
            }
            catch { }
        }

        private void textBox3_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textboxName = (TextBox)this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
            char[] nameArray = this.ActiveControl.Name.ToCharArray();
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
            string n = new string(nameArray);
            TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 2);
            n = new string(nameArray);
            TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

            if (e.KeyCode == Keys.Right && textboxName.SelectionStart == textboxName.Text.Length)
            {
                textboxNextName.Focus();
            }
            else if (e.KeyCode == Keys.Left && textboxName.SelectionStart == 0)
            {
                textboxLastName.Focus();
            }
            else if (e.KeyCode == Keys.Enter && nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] < '4')
            {
                button1_Click(sender, e);
            }
        }

        private void textBox3_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                TextBox textboxName = (TextBox)this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                char[] nameArray = this.ActiveControl.Name.ToCharArray();
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] + 1);
                string n = new string(nameArray);
                TextBox textboxNextName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 2);
                n = new string(nameArray);
                TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

                if (e.KeyChar != '\b')
                {
                    if (e.KeyChar < '0' || e.KeyChar > '9')
                    {
                        e.Handled = true;
                        if (e.KeyChar == '.' && textboxName.Text.Length > 0)
                        {
                            textboxNextName.Focus();
                        }
                    }
                    if (textboxName.Text.Length >= 2)
                    {
                        textboxNextName.Focus();
                    }
                }
                else
                {
                    if (textboxName.SelectionStart == 0)
                    {
                        textboxLastName.Focus();
                    }
                }
            }
            catch { }
        }

        private void textBox4_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox textboxName = (TextBox)sender;
            char[] nameArray = this.ActiveControl.Name.ToCharArray();
            nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 1);
            string n = new string(nameArray);
            TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

            if (e.KeyCode == Keys.Left && textboxName.SelectionStart == 0)
            {
                textboxLastName.Focus();
            }
            else if (e.KeyCode == Keys.Enter && nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] < '4')
            {
                button1_Click(sender, e);
            }
        }

        private void textBox4_KeyPress(object sender, KeyPressEventArgs e)
        {
            try
            {
                TextBox textboxName = (TextBox)this.GetType().GetField(this.ActiveControl.Name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);
                char[] nameArray = this.ActiveControl.Name.ToCharArray();
                nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] = (char)(nameArray[int.Parse(this.ActiveControl.Name.Length.ToString()) - 1] - 1);
                string n = new string(nameArray);
                TextBox textboxLastName = (TextBox)this.GetType().GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase).GetValue(this);

                if (e.KeyChar != '\b')
                {
                    if (e.KeyChar < '0' || e.KeyChar > '9')
                    {
                        e.Handled = true;
                    }
                }
                else
                {
                    if (textboxName.SelectionStart == 0)
                    {
                        textboxLastName.Focus();
                    }
                }
            }
            catch { }
        }

        private void textBox26_KeyPress(object sender, KeyPressEventArgs e)
        {
            if ((e.KeyChar < '0' || e.KeyChar > '9') && e.KeyChar != '\b')
            {
                e.Handled = true;
            }
        }

        private void comboBox6_MouseDown(object sender, MouseEventArgs e)
        {
            comboBox6.Items.Clear();
            comboBox6.Items.AddRange(System.IO.Ports.SerialPort.GetPortNames());
        }

        private void comboBox2_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                label37.Text = lora_rate[int.Parse(comboBox2.SelectedItem.ToString()), 0].ToString();
                label38.Text = lora_rate[int.Parse(comboBox2.SelectedItem.ToString()), 1].ToString();
                label40.Text = lora_rate[int.Parse(comboBox2.SelectedItem.ToString()), 2].ToString();
            }
            catch
            {
                label37.Text = "";
                label38.Text = "";
                label40.Text = "";
            }
        }

        private void radioButton1_CheckedChanged(object sender, EventArgs e)
        {
            if(radioButton1.Checked == true)
            {
                printMessage("ip mode.\r\n", "blue");
                ConMode = CONNECT_MODE_IP;
                textBox1.ReadOnly = false;
                textBox2.ReadOnly = false;
                textBox3.ReadOnly = false;
                textBox4.ReadOnly = false;
                textBox26.ReadOnly = false;
                comboBox4.Enabled = false;
                comboBox5.Enabled = false;
                comboBox6.Enabled = false;
                comboBox7.Enabled = false;
                comboBox8.Enabled = false;
                radioButton3.Enabled = false;
                radioButton4.Enabled = false;
                radioButton3.Checked = true;
                textBox1.Focus();
            }
            else
            {
                ConMode = CONNECT_MODE_COM;
                textBox1.ReadOnly = true;
                textBox2.ReadOnly = true;
                textBox3.ReadOnly = true;
                textBox4.ReadOnly = true;
                textBox26.ReadOnly = true;
                comboBox4.Enabled = true;
                comboBox5.Enabled = true;
                comboBox6.Enabled = true;
                comboBox7.Enabled = true;
                comboBox8.Enabled = true;
                radioButton3.Enabled = true;
                radioButton4.Enabled = true;
            }
        }

        private void radioButton2_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton2.Checked == true)
            {
                printMessage("serial mode.\r\n", "blue");
                ConMode = CONNECT_MODE_COM;
                textBox1.ReadOnly = true;
                textBox2.ReadOnly = true;
                textBox3.ReadOnly = true;
                textBox4.ReadOnly = true;
                textBox26.ReadOnly = true;
                comboBox4.Enabled = true;
                comboBox5.Enabled = true;
                comboBox6.Enabled = true;
                comboBox7.Enabled = true;
                comboBox8.Enabled = true;
                radioButton3.Enabled = true;
                radioButton4.Enabled = true;
            }
            else
            {
                ConMode = CONNECT_MODE_IP;
                textBox1.ReadOnly = false;
                textBox2.ReadOnly = false;
                textBox3.ReadOnly = false;
                textBox4.ReadOnly = false;
                textBox26.ReadOnly = false;
                comboBox4.Enabled = false;
                comboBox5.Enabled = false;
                comboBox6.Enabled = false;
                comboBox7.Enabled = false;
                comboBox8.Enabled = false;
                radioButton3.Enabled = false;
                radioButton4.Enabled = false;
            }
        }

        private void textBox26_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if (int.Parse(textBox26.Text) > 65535)
                {
                    textBox26.Text = "";
                    MessageBox.Show("错误端口！");
                }
            }
            catch { }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (Constate == CONNECTED)
            {
                if (cntTimeCount <= 0)
                {
                    printMessage("开始查找附近可配置的设备.\r\n", "blue");
                    cntTimeCount = 31;
                    cnttimer.Start();
                }
            }
            else
            {
                MessageBox.Show("设备未连接！");
            }
        }

        private void button10_Click(object sender, EventArgs e)
        {
            cntTimeCount = 0;
        }

        private void radioButton4_CheckedChanged(object sender, EventArgs e)
        {
            if(radioButton4.Checked == true)
            {
                typedev = DEVICE_BASE;
                radioButton2.Checked = true;
                button3.Enabled  = false;
                button4.Enabled  = false;
                button7.Enabled  = false;
                button2.Enabled  = false;
                button10.Enabled = false;
            }
            else
            {
                button3.Enabled  = true;
                button4.Enabled  = true;
                button7.Enabled  = true;
                button2.Enabled  = true;
                button10.Enabled = true;
            }
        }

        private void radioButton3_CheckedChanged(object sender, EventArgs e)
        {
            if (radioButton3.Checked == true)
            {
                typedev = DEVICE_GATW;
                button3.Enabled = true;
                button4.Enabled = true;
                button7.Enabled = true;
                button2.Enabled = true;
                button10.Enabled = true;
            }
            else
            {
                button3.Enabled = false;
                button4.Enabled = false;
                button7.Enabled = false;
                button2.Enabled = false;
                button10.Enabled = false;
            }
        }

        private void textBox5_TextChanged(object sender, EventArgs e)
        {
            try
            {
                if(int.Parse(textBox5.Text) > 255)
                {
                    textBox5.Text = "1";
                    MessageBox.Show("输入错误，不能大于255");
                }
            }
            catch { }
        }
    }
}

