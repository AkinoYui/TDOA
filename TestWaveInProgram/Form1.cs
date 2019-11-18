using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using WinMM;
using System.Numerics;

namespace TestWaveInProgram
{
    public partial class Form1 : Form
    {
        List<WaveInDeviceCaps> waveInDevice=new List<WaveInDeviceCaps>();
        List<WaveIn> waveIn=new List<WaveIn>();
        WaveFormat format;
        List<ComboBox> cb = new List<ComboBox>();

        List<int> waveRec1;
        List<int> waveRec2;

        int fCount=0;
        int bCount = 20000;
        int []peakDelay=new int[2];
        double doa;
        Point pt4;

        bool isSetting = false;

        public Form1()
        {
            InitializeComponent();

            //장치 목록 업데이트
            cb.Add(comboBox1);
            cb.Add(comboBox2);

            for (int i=0; i < 2; i++){
                cb[i].Items.Clear();
            }
            waveRec1 = new List<int>();
            waveRec2 = new List<int>();
            
            var devices = WaveIn.Devices;
            foreach (WaveInDeviceCaps cap in devices)
            {
                comboBox1.Items.Add(cap.Name);
                comboBox2.Items.Add(cap.Name);
            }
            
            this.FormClosing += Form1_FormClosing;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            for(int i=0;i<waveIn.Count;i++)
                if (waveIn[i] != null)
                    waveIn[i].Dispose();
        }

        //필요 없음 아래로 ㄱ
        private void ComboBox_device1_SelectedIndexChanged(object sender, EventArgs e)
        {
            button_record1.Enabled = true;
        }

        public void recWave(int index)
        {
            try
            {
                //인덱스 가져오기
                waveInDevice.Add(WaveIn.Devices[cb[index].SelectedIndex]);

                format = new WaveFormat
                {
                    //16bit PCM Mono 44100 Hz
                    FormatTag = WaveFormatTag.Pcm,
                    BitsPerSample = 16,
                    Channels = 1,
                    SamplesPerSecond = 48000
                };

                //장치 설정 후 녹음 시작
                waveIn.Add(new WaveIn(waveInDevice[index].DeviceId, 480)); //버퍼에 512 샘플이 모이면 이벤트 발생
                waveIn[index].Open(format);
                if(index==0)
                    waveIn[index].DataReady += WaveIn1_DataReady;//이벤트 함수
                else if (index == 1)
                    waveIn[index].DataReady += WaveIn2_DataReady;//이벤트 함수
                waveIn[index].Start();
            }
            catch (Exception exception)
            {
                //귀찮으니 에러 한 방에 모아서 해결
                MessageBox.Show(exception.Message);
            }
        }

        //녹음 시작
        private void Button_record1_Click(object sender, EventArgs e)
        {
            comboBox1.Enabled = false;
            label1.Enabled = false;
            comboBox2.Enabled = false;
            label2.Enabled = false;
            textBox1.Visible = true;
            textBox1.Location = new Point(640,650);
            
            pt4= new Point(pictureBox3.Location.X+ pictureBox3.Width/2-pictureBox4.Width/2-8
                                           , pictureBox3.Location.Y + pictureBox3.Height / 2 - pictureBox4.Height / 2-240);
            pictureBox4.Visible = true;

            recWave(0);
            recWave(1);

            button_record1.Enabled = false;
        }


        //주어진 pictureBox에 wave를 그리는 함수
        public void DrawWave(PictureBox pb, List<int> waveRec)
        {
            Image background = pb.Image;
            Bitmap bitmap = new Bitmap(pb.Width, pb.Height);
            int SamplesPerSecond = format.SamplesPerSecond;
            int Second = 1;

            for (int i = 0; i < bitmap.Width; i++)
            {
                if (waveRec.Count - SamplesPerSecond * Second + (int)((double)i / bitmap.Width * SamplesPerSecond * Second) - 1 < 0)
                    bitmap.SetPixel(i, bitmap.Height / 2 - 1, Color.Black);
                else if((int)(-waveRec[waveRec.Count - SamplesPerSecond * Second + (int)((double)i / bitmap.Width * SamplesPerSecond * Second)] / 32768.0 * bitmap.Height / 2 + bitmap.Height / 2 - 1) < 0)
                    bitmap.SetPixel(i, 0, Color.Black);
                else if ((int)(-waveRec[waveRec.Count - SamplesPerSecond * Second + (int)((double)i / bitmap.Width * SamplesPerSecond * Second)] / 32768.0 * bitmap.Height / 2 + bitmap.Height / 2 - 1) > bitmap.Height)
                    bitmap.SetPixel(i, bitmap.Height, Color.Black);
                else
                    bitmap.SetPixel(i, (int)(-waveRec[waveRec.Count - SamplesPerSecond * Second + (int)((double)i / bitmap.Width * SamplesPerSecond * Second)] / 32768.0 * bitmap.Height / 2 + bitmap.Height / 2 - 1), Color.Black);
            }

            pb.Image = bitmap;
            if (background != null)
                background.Dispose();
        }
        //버퍼에 데이터가 모일 때 마다 발생
        private void WaveIn1_DataReady(object sender, DataReadyEventArgs e)
        {
            for (int i = 0; i < e.Data.Length / 2; i++)
            {
                waveRec1.Add(BitConverter.ToInt16(e.Data, i * 2));
            }
            //DrawWave(pictureBox1, waveRec1);    
        }
        private void WaveIn2_DataReady(object sender, DataReadyEventArgs e)
        {
            for (int i = 0; i < e.Data.Length / 2; i++)
            {
                waveRec2.Add(BitConverter.ToInt16(e.Data, i * 2));
            }
            //DrawWave(pictureBox2, waveRec2);
        }

        public void Movemean(double[] arr, int d) {
            double sum;
            Array.Resize(ref arr, arr.Length + d);//예외처리를 위해 배열길이를 d 늘려서 마지막값으로 초기화함
            for (int i = 0; i < d; i++)
                arr[i + arr.Length-d] = arr[arr.Length-d - 1];

            for (int i = 0; i < arr.Length-d; i++)
            {
                sum = 0;
                for (int k = 0; k < d; k++)
                {
                    sum += arr[i + k];
                }
                arr[i] = sum/d;
            }
            Array.Resize(ref arr, arr.Length - d);
        }
        public void Medfilt1(double[] arr)
        {
            double[] tarr = new double[5];
            Array.Resize(ref arr,arr.Length+5);//예외처리를 위해 배열길이를 5 늘려서 0값으로 초기화함
            for (int i = 0; i < 5; i++)
                arr[i + arr.Length-5] = 0;

            for (int k = 0; k < arr.Length-5; k++)
            {
                for (int i = 0; i < 5; i++)
                {
                    Array.Copy(arr, k, tarr,0, 5);//arr에 남은 범위가 5가 안될 때 예외처리해야함 
                    Array.Sort(tarr, 0, 5);//tarr 정렬에서 중간값찾기
                    arr[k] = tarr[2];
                }
            }
            Array.Resize(ref arr, arr.Length - 5);
        }
        public int DelayIDX(List<int> m1, List<int> m2)
        {
            Complex[] complex1, complex2, complex3;

            complex1 = new Complex[LeastSquare(m1.Count) * 2];
            complex2 = new Complex[LeastSquare(m2.Count) * 2];

            for (int i = 0; i < m1.Count; i++)
                complex1[i] = m1[i];
            for (int i = 0; i < m2.Count; i++)
                complex2[m2.Count - 1 - i] = m2[i];

            //fft( , true)는 그냥 fft이며 false를 넣으면 ifft가 된다.
            FFT.Radix2(ref complex1, true);
            FFT.Radix2(ref complex2, true);

            int complex_length = complex1.Length;
            complex3 = new Complex[complex_length];

            for (int i = 0; i < complex_length; i++)
                complex3[i] = complex1[i] * complex2[i];

            FFT.Radix2(ref complex3, false);

            List<double> result = new List<double>();
            for (int i = 0; i < complex_length; i++)
                result.Add(complex3[i].Real);

            // cmp2를 역정렬했기 때문에 전체시간에서 피크시간값을 빼야 원하는 시간차값이 나온다
            return (m1.Count - result.IndexOf(result.Max()));
        }

        public int CalcIDX(List<int> m1, List<int> m2)
        {
            Complex[] complex1, complex2, complex3;

            complex1 = new Complex[LeastSquare(m1.Count) * 2];
            complex2 = new Complex[LeastSquare(m2.Count) * 2];

            for (int i = 0; i < m1.Count; i++)
                complex1[i] = m1[i];
            for (int i = 0; i < m2.Count; i++)
                complex2[m2.Count - 1 - i] = m2[i];

            //fft( , true)는 그냥 fft이며 false를 넣으면 ifft가 된다.
            FFT.Radix2(ref complex1, true);
            FFT.Radix2(ref complex2, true);

            int complex_length = complex1.Length;
            complex3 = new Complex[complex_length];

            for (int i = 0; i < complex_length; i++)
                complex3[i] = complex1[i] * complex2[i];
            // /abs(complex3[i]*complex[i]) 넣어주면 GCC PHAT(아마)

            FFT.Radix2(ref complex3, false);

            List<double> result = new List<double>();
            for (int i = 0; i < complex_length; i++)
                result.Add(complex3[i].Real);

            // cmp2를 역정렬했기 때문에 전체시간에서 피크시간값을 빼야 원하는 시간차값이 나온다
            return result.IndexOf(result.Max());
        }

        int LeastSquare(int l)
        {
            int n = 1;
            while (l > n)
                n *= 2;
            return n;
        }
        int MinCount()
        {
            List<int> counts = new List<int>();
            counts.Add(waveRec1.Count);
            counts.Add(waveRec2.Count);
            return counts.Min();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void Timer1_Tick(object sender, EventArgs e)
        {
            if (MinCount()-bCount > 0)
            {
                if (isSetting)
                {
                    List<int> peak = new List<int>();
                    int p12, min;
                    p12 = DelayIDX(waveRec1.GetRange(MinCount() - bCount, bCount), waveRec2.GetRange(MinCount() - bCount, bCount));

                    peak.Add(0);
                    peak.Add(-p12);
                    min = peak.Min();
                    for (int i = 0; i < peak.Count; i++)
                        peakDelay[i] =peak[i] - min;

                    richTextBox3.Text = peakDelay[0].ToString() + "\t" + peakDelay[1].ToString();
                }
                else
                {
                    /*doa = RoundTDOA(waveRec1.GetRange(MinCount() - bCount, bCount),
                    waveRec2.GetRange(MinCount() - bCount, bCount),
                    waveRec3.GetRange(MinCount() - bCount, bCount),
                    waveRec4.GetRange(MinCount() - bCount, bCount));//20000크기로 배열을 만들어 보내기
                    //richTextBox1.Text = doa.ToString();*/

                    double pp12 = DelayIDX(waveRec1.GetRange(MinCount() -peakDelay[0]- bCount, bCount), waveRec2.GetRange(MinCount() - peakDelay[1] - bCount, bCount));
                    if (pp12 > 35)//pp34 계산법 d * 48000 /340
                        pp12 = 35;
                    else if (pp12 < -35)
                    {
                        pp12 = -35;
                    }
                    else
                    {
                        //richTextBox1.Text = (Math.Asin(340 * pp12 / (double)format.SamplesPerSecond / 0.25) * 180 / Math.PI).ToString();
                        doa= Math.Asin(340 * pp12 / (double)format.SamplesPerSecond / 0.25) * 180 / Math.PI;
                    }//radian = Math.Asin(340 * idx / samplepersecond / 0.26) * 180 / Math.PI;
                    textBox1.Text = doa.ToString("N2");
                    pictureBox4.Location = new Point((int)(370*Math.Cos((doa+90)*Math.PI/180))+pt4.X
                                                    , (int)(370 * Math.Sin((doa + 90) * Math.PI / 180))+ pt4.Y);

                }
                fCount++;
                //데이터 관리
                if(waveRec1.Count>400000&& waveRec2.Count > 400000)
                {
                    waveRec1.RemoveRange(0, 100000);
                    waveRec2.RemoveRange(0, 100000);
                }
                //richTextBox4.Text = "Tic : " + fCount.ToString() + "\nNOW : "+ (MinCount() - bCount).ToString() +"\nMIN : "+MinCount().ToString()+ "\nw1c : " + waveRec1.Count.ToString() + "\nw2c : " + waveRec2.Count.ToString();
            }
        }
        
        private void Button1_Click(object sender, EventArgs e)
        {
            if(isSetting)
            {
                //peakDelay만큼 remove하는 함수를 앞쪽에 넣을 수 있다.
                isSetting = false;
                button1.Text = "Setting";
                richTextBox3.Enabled = false;
                //for(int i = 0; i < peakDelay[0]; i++)
                //    waveRec1.Insert(0, 0);
                //for (int i = 0; i < peakDelay[1]; i++)
                //    waveRec2.Insert(0, 0);
            }
            else
            {
                isSetting = true;
                button1.Text = "Stop";
                richTextBox3.Enabled = true;
                //for (int i = 0; i < peakDelay[0]; i++)
                //    waveRec1.RemoveAt(0);
                //for (int i = 0; i < peakDelay[1]; i++)
                //    waveRec2.RemoveAt(0);
                //peakDelay[0] = 0;
                //peakDelay[1] = 0;
            }
        }
    }
}
