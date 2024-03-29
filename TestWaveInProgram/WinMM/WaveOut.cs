﻿//-----------------------------------------------------------------------
// <copyright file="WaveOut.cs" company="(none)">
//  Copyright (c) 2009 John Gietzen
//
//  Permission is hereby granted, free of charge, to any person obtaining
//  a copy of this software and associated documentation files (the
//  "Software"), to deal in the Software without restriction, including
//  without limitation the rights to use, copy, modify, merge, publish,
//  distribute, sublicense, and/or sell copies of the Software, and to
//  permit persons to whom the Software is furnished to do so, subject to
//  the following conditions:
//
//  The above copyright notice and this permission notice shall be
//  included in all copies or substantial portions of the Software.
//
//  THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
//  EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
//  MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
//  NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS
//  BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN
//  ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
//  CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
//  SOFTWARE
// </copyright>
// <author>John Gietzen</author>
//-----------------------------------------------------------------------

namespace WinMM
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Xml;

    /// <summary>
    /// Encapsulates the waveOut commands in the <see cref="NativeMethods"/> class (from winmm.dll).  This provides a familiar format for using the WaveOut tools.
    /// </summary>
    public sealed class WaveOut : IDisposable
    {
        /// <summary>
        /// Indicates the DeviceID of the Microsoft Wave Mapper device.
        /// </summary>
        public const int WaveOutMapperDeviceId = -1;

        /// <summary>
        /// Holds a list of manufactureres, read lazily from the assembly's resources.
        /// </summary>
        private static XmlDocument manufacturers;

        /// <summary>
        /// Holds this device's DeviceID.
        /// </summary>
        private int deviceId;

        /// <summary>
        /// Holds the device's capabilities.
        /// </summary>
        private WaveOutDeviceCaps capabilities;

        /// <summary>
        /// Hold a locking object for start/stop synchronization.
        /// </summary>
        private object startStopLock = new object();

        /// <summary>
        /// Hold a locking object for buffer synchronization.
        /// </summary>
        private object bufferingLock = new object();

        /// <summary>
        /// Holds a flag indicating whether or not we are currently buffering.
        /// </summary>
        private bool buffering;

        /// <summary>
        /// Holds the number of buffers currently in the queue.
        /// </summary>
        private int bufferQueueCount;

        /// <summary>
        /// Holds a list of buffers to be released to the operating system.
        /// </summary>
        private Queue<IntPtr> bufferReleaseQueue = new Queue<IntPtr>();

        /// <summary>
        /// Holds the thread used to release completed buffers and add new buffers to the queue.
        /// </summary>
        private Thread bufferMaintainerThread;

        /// <summary>
        /// Holds the handle to the device.
        /// </summary>
        private WaveOutSafeHandle handle;

        /// <summary>
        /// Holds a reference to our our own callback.
        /// </summary>
        /// <remarks>
        /// We assign this a value in the constructor, and maintain it until at lease after either
        /// Dispose or the Finalizer is called to prevent the garbage collector from finalizing
        /// the instance we pass to the <see cref="NativeMethods.waveOutOpen"/> method.
        /// </remarks>
        private NativeMethods.waveOutProc callback;

        /// <summary>
        /// Initializes a new instance of the WaveOut class, based on an available Device Id.
        /// </summary>
        /// <param name="deviceId">The device identifier to obtain.</param>
        /// <exception cref="ArgumentOutOfRangeException">If <paramref name="deviceId"/> is not in the valid range.</exception>
        public WaveOut(int deviceId)
        {
            if (deviceId >= DeviceCount && deviceId != WaveOutMapperDeviceId)
            {
                throw new ArgumentOutOfRangeException("deviceId", "The Device ID specified was not within the valid range.");
            }

            this.callback = new NativeMethods.waveOutProc(this.InternalCallback);

            this.deviceId = (int)deviceId;
        }

        /// <summary>
        /// Finalizes an instance of the WaveOut class and disposes of the native resources used by the instance.
        /// </summary>
        ~WaveOut()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Called when the device recieves a message from the system.
        /// </summary>
        public event EventHandler<WaveOutMessageReceivedEventArgs> MessageReceived;

        /// <summary>
        /// Gets the devices offered by the system.
        /// </summary>
        public static ReadOnlyCollection<WaveOutDeviceCaps> Devices
        {
            get { return GetAllDeviceCaps().AsReadOnly(); }
        }

        /// <summary>
        /// Gets this device's capabilities.
        /// </summary>
        public WaveOutDeviceCaps Capabilities
        {
            get
            {
                if (this.capabilities == null)
                {
                    this.capabilities = GetDeviceCaps(this.deviceId);
                }

                return this.capabilities;
            }
        }

        /// <summary>
        /// Gets or sets the stereo volume of the device.
        /// </summary>
        /// <remarks>
        /// If the device does not support sereo volume, the left channel will be used for the mono volume.
        /// </remarks>
        public Volume Volume
        {
            get
            {
                uint volume = 0;
                if (this.handle != null && !this.handle.IsInvalid && !this.handle.IsClosed)
                {
                    NativeMethods.Throw(
                        NativeMethods.waveOutGetVolume(this.handle, ref volume),
                        NativeMethods.ErrorSource.WaveOut);
                }
                else
                {
                    NativeMethods.Throw(
                        NativeMethods.waveOutGetVolume((UIntPtr)(uint)this.deviceId, ref volume),
                        NativeMethods.ErrorSource.WaveOut);
                }

                uint left = volume & (uint)0xFFFF;
                uint right = volume >> 16;
                Volume ret = new Volume();
                ret.Left = (float)left / UInt16.MaxValue;
                ret.Right = (float)right / UInt16.MaxValue;
                return ret;
            }

            set
            {
                float leftVolume = Math.Min(Math.Max(value.Left, 0.0f), 1.0f);
                float rightVolume = Math.Min(Math.Max(value.Right, 0.0f), 1.0f);
                uint left = (uint)(UInt16.MaxValue * leftVolume);
                uint right = (uint)(UInt16.MaxValue * rightVolume);
                uint volume = left | (right << 16);

                if (this.handle != null && !this.handle.IsInvalid && !this.handle.IsClosed)
                {
                    NativeMethods.Throw(
                        NativeMethods.waveOutSetVolume(this.handle, volume),
                        NativeMethods.ErrorSource.WaveOut);
                }
                else
                {
                    NativeMethods.Throw(
                        NativeMethods.waveOutSetVolume((UIntPtr)(uint)this.deviceId, volume),
                        NativeMethods.ErrorSource.WaveOut);
                }
            }
        }

        /// <summary>
        /// Gets or sets the pitch modifier ratio.  This is not supported on all devices.
        /// </summary>
        public float Pitch
        {
            get
            {
                uint pitch = 0;
                NativeMethods.Throw(
                    NativeMethods.waveOutGetPitch(this.handle, ref pitch),
                    NativeMethods.ErrorSource.WaveOut);

                return FixedToFloat(pitch);
            }

            set
            {
                NativeMethods.Throw(
                    NativeMethods.waveOutSetPitch(this.handle, FloatToFixed(value)),
                    NativeMethods.ErrorSource.WaveOut);
            }
        }

        /// <summary>
        /// Gets or sets the playback rate modifier ratio.  This is not supported on all devices.
        /// </summary>
        public float PlaybackRate
        {
            get
            {
                uint rate = 0;
                NativeMethods.Throw(
                    NativeMethods.waveOutGetPlaybackRate(this.handle, ref rate),
                    NativeMethods.ErrorSource.WaveOut);

                return FixedToFloat(rate);
            }

            set
            {
                NativeMethods.Throw(
                    NativeMethods.waveOutSetPlaybackRate(this.handle, FloatToFixed(value)),
                    NativeMethods.ErrorSource.WaveOut);
            }
        }

        /// <summary>
        /// Gets the number of devices available on the system.
        /// </summary>
        private static int DeviceCount
        {
            get
            {
                return (int)NativeMethods.waveOutGetNumDevs();
            }
        }

        /// <summary>
        /// Gets a document containing the names of all of the device manufactureres.
        /// </summary>
        private static XmlDocument Manufacturers
        {
            get
            {
                if (manufacturers == null)
                {
                    XmlDocument doc = new XmlDocument();

                    string result = string.Empty;
                    string[] names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
                    string name = Array.FindAll(names, s => s.Contains("Devices.xml"))[0];
                    using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    {
                        using (StreamReader sr = new StreamReader(stream))
                        {
                            result = sr.ReadToEnd();
                            doc.LoadXml(result);
                        }
                    }
                    manufacturers = doc;
                }

                return manufacturers;
            }
        }

        /// <summary>
        /// Opens the device for writing with the specified format.
        /// </summary>
        /// <param name="waveFormat">The format of the device to open.</param>
        public void Open(WaveFormat waveFormat)
        {
            lock (this.startStopLock)
            {
                if (this.handle != null)
                {
                    throw new InvalidOperationException("The device is already open.");
                }

                NativeMethods.WAVEFORMATEX wfx = new NativeMethods.WAVEFORMATEX();
                wfx.nAvgBytesPerSec = waveFormat.AverageBytesPerSecond;
                wfx.wBitsPerSample = waveFormat.BitsPerSample;
                wfx.nBlockAlign = waveFormat.BlockAlign;
                wfx.nChannels = waveFormat.Channels;
                wfx.wFormatTag = (short)(int)waveFormat.FormatTag;
                wfx.nSamplesPerSec = waveFormat.SamplesPerSecond;
                wfx.cbSize = 0;

                IntPtr tempHandle = new IntPtr();
                NativeMethods.Throw(
                    NativeMethods.waveOutOpen(
                        ref tempHandle,
                        (uint)this.deviceId,
                        ref wfx,
                        this.callback,
                        (IntPtr)0,
                        NativeMethods.WAVEOPENFLAGS.CALLBACK_FUNCTION | NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_DIRECT),
                    NativeMethods.ErrorSource.WaveOut);
                this.handle = new WaveOutSafeHandle(tempHandle);

                lock (this.bufferingLock)
                {
                    this.buffering = true;
                    Monitor.Pulse(this.bufferingLock);
                }

                this.bufferMaintainerThread = new Thread(new ThreadStart(this.MaintainBuffers));
                this.bufferMaintainerThread.IsBackground = true;
                this.bufferMaintainerThread.Name = "WaveOut MaintainBuffers thread. (DeviceID = " + this.deviceId + ")";
                this.bufferMaintainerThread.Start();
            }
        }

        /// <summary>
        /// Closes the device.  If the device is playing, playback is stopped.
        /// </summary>
        public void Close()
        {
            lock (this.startStopLock)
            {
                if (this.handle != null)
                {
                    if (!this.handle.IsClosed && !this.handle.IsInvalid)
                    {
                        this.Stop();

                        lock (this.bufferingLock)
                        {
                            this.buffering = false;
                            Monitor.Pulse(this.bufferingLock);
                        }

                        this.bufferMaintainerThread.Join();

                        this.handle.Close();
                        this.handle = null;
                    }
                }
            }
        }

        /// <summary>
        /// Writes a block of data (in the current forma, set during Open) to the device.
        /// </summary>
        /// <param name="bufferData">The data to send to the device.</param>
        public void Write(byte[] bufferData)
        {
            lock (this.startStopLock)
            {
                IntPtr mem = Marshal.AllocHGlobal(bufferData.Length);
                Marshal.Copy(bufferData, 0, mem, bufferData.Length);

                NativeMethods.WAVEHDR pwh = new NativeMethods.WAVEHDR();
                pwh.dwBufferLength = (uint)bufferData.Length;
                pwh.dwFlags = 0;
                pwh.lpData = mem;
                pwh.dwUser = new IntPtr(12345);

                IntPtr header = Marshal.AllocHGlobal(Marshal.SizeOf(pwh));
                Marshal.StructureToPtr(pwh, header, false);

                NativeMethods.Throw(
                    NativeMethods.waveOutPrepareHeader(this.handle, header, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))),
                    NativeMethods.ErrorSource.WaveOut);

                NativeMethods.Throw(
                    NativeMethods.waveOutWrite(this.handle, header, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))),
                    NativeMethods.ErrorSource.WaveOut);

                lock (this.bufferingLock)
                {
                    this.bufferQueueCount++;
                    Monitor.Pulse(this.bufferingLock);
                }
            }
        }

        /// <summary>
        /// Pauses playback.  If playback is already paused, this does nothing.
        /// </summary>
        public void Pause()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(
                    NativeMethods.waveOutPause(this.handle),
                    NativeMethods.ErrorSource.WaveOut);
            }
        }

        /// <summary>
        /// Resumes playback.  If playback is already in progress, this does nothing.
        /// </summary>
        public void Resume()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(
                    NativeMethods.waveOutRestart(this.handle),
                    NativeMethods.ErrorSource.WaveOut);
            }
        }

        /// <summary>
        /// Stops playback.
        /// </summary>
        public void Stop()
        {
            lock (this.startStopLock)
            {
                NativeMethods.Throw(
                    NativeMethods.waveOutReset(this.handle),
                    NativeMethods.ErrorSource.WaveOut);
            }
        }

        /// <summary>
        /// Determines whether or not the device supports a given format.
        /// </summary>
        /// <param name="waveFormat">The format to check.</param>
        /// <returns>true, if the format is supported; false, otherwise.</returns>
        public bool SupportsFormat(WaveFormat waveFormat)
        {
            NativeMethods.WAVEFORMATEX wfx = new NativeMethods.WAVEFORMATEX();
            wfx.nAvgBytesPerSec = waveFormat.AverageBytesPerSecond;
            wfx.wBitsPerSample = waveFormat.BitsPerSample;
            wfx.nBlockAlign = waveFormat.BlockAlign;
            wfx.nChannels = waveFormat.Channels;
            wfx.wFormatTag = (short)(int)waveFormat.FormatTag;
            wfx.nSamplesPerSec = waveFormat.SamplesPerSecond;
            wfx.cbSize = 0;

            IntPtr dummy = new IntPtr(0);
            NativeMethods.MMSYSERROR ret = NativeMethods.waveOutOpen(
                ref dummy,
                (uint)this.deviceId,
                ref wfx,
                null,
                (IntPtr)0,
                NativeMethods.WAVEOPENFLAGS.WAVE_FORMAT_QUERY);

            if (ret == NativeMethods.MMSYSERROR.MMSYSERR_NOERROR)
            {
                return true;
            }
            else if (ret == NativeMethods.MMSYSERROR.WAVERR_BADFORMAT)
            {
                return false;
            }
            else
            {
                NativeMethods.Throw(ret, NativeMethods.ErrorSource.WaveOut);
                return false;
            }
        }

        /// <summary>
        /// Disposes of the managed and native resources used by this instance.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Retrieves the capabilities of a device.
        /// </summary>
        /// <param name="deviceId">The DeviceID for which to retrieve the capabilities.</param>
        /// <returns>The capabilities of the device.</returns>
        private static WaveOutDeviceCaps GetDeviceCaps(int deviceId)
        {
            NativeMethods.WAVEOUTCAPS wocaps = new NativeMethods.WAVEOUTCAPS();
            NativeMethods.waveOutGetDevCaps(new UIntPtr((uint)deviceId), ref wocaps, (uint)Marshal.SizeOf(wocaps.GetType()));
            WaveOutDeviceCaps caps = new WaveOutDeviceCaps();
            caps.DeviceId = (int)deviceId;
            caps.Channels = wocaps.wChannels;
            caps.DriverVersion = (int)wocaps.vDriverVersion;
            caps.Manufacturer = GetManufacturer(wocaps.wMid);
            caps.Name = wocaps.szPname;
            caps.ProductId = wocaps.wPid;
            caps.Capabilities = wocaps.dwSupport;

            return caps;
        }

        /// <summary>
        /// Retreives a manufacturer's name from the manufacturer registry resource.
        /// </summary>
        /// <param name="manufacturerId">The ManufacturerID for which to search.</param>
        /// <returns>The specified manufacturer's name.</returns>
        private static string GetManufacturer(ushort manufacturerId)
        {
            XmlDocument manufacturers = Manufacturers;
            XmlElement man = null;

            if (manufacturers != null)
            {
                man = (XmlElement)manufacturers.SelectSingleNode("/devices/manufacturer[@id='" + manufacturerId.ToString(CultureInfo.InvariantCulture) + "']");
            }
            
            if (man == null)
            {
                return "Unknown [" + manufacturerId + "]";
            }

            return man.GetAttribute("name");
        }

        /// <summary>
        /// Converts a floating point number to a 32-bit fixed point number.
        /// </summary>
        /// <param name="value">The floating point number to convert.</param>
        /// <returns>A 32-bit fixed point number.</returns>
        private static uint FloatToFixed(float value)
        {
            short whole = (short)value;
            ushort fraction = (ushort)((value - whole) * ushort.MaxValue);

            return (((uint)whole) << 8) | (((uint)fraction) >> 8);
        }

        /// <summary>
        /// Converts a 32-bit fixed point number number to a floating point.
        /// </summary>
        /// <param name="value">The 32-bit fixed point number to convert.</param>
        /// <returns>A floating point number.</returns>
        private static float FixedToFloat(uint value)
        {
            short whole = (short)(value >> 8);
            ushort fraction = (ushort)value;

            return (float)whole + (((float)fraction) / ushort.MaxValue);
        }

        /// <summary>
        /// Retrieves a list of the capabilities of all of the devices registered on the system.
        /// </summary>
        /// <returns>A list of the capabilities of all of the devices registered on the system.</returns>
        private static List<WaveOutDeviceCaps> GetAllDeviceCaps()
        {
            List<WaveOutDeviceCaps> devices = new List<WaveOutDeviceCaps>();
            int count = DeviceCount;
            
            for (int i = 0; i < count; i++)
            {
                devices.Add(GetDeviceCaps(i));
            }

            devices.Add(GetDeviceCaps(WaveOutMapperDeviceId));

            return devices;
        }

        /// <summary>
        /// Fires when the operating system has a message about a device.
        /// </summary>
        /// <param name="waveOutHandle">A handle to the device on which the message has been fired.</param>
        /// <param name="message">The message to be processed.</param>
        /// <param name="instance">A user instance value.</param>
        /// <param name="param1">Message parameter one.</param>
        /// <param name="param2">Message parameter two.</param>
        private void InternalCallback(IntPtr waveOutHandle, NativeMethods.WAVEOUTMESSAGE message, IntPtr instance, IntPtr param1, IntPtr param2)
        {
            if (message == NativeMethods.WAVEOUTMESSAGE.WOM_DONE)
            {
                lock (this.bufferingLock)
                {
                    this.bufferReleaseQueue.Enqueue(param1);
                    this.bufferQueueCount--;
                    Monitor.Pulse(this.bufferingLock);
                }
            }

            if (this.MessageReceived != null)
            {
                this.MessageReceived(this, new WaveOutMessageReceivedEventArgs((WaveOutMessage)message));
            }
        }

        /// <summary>
        /// Adds buffers to the device and cleans up completed buffers.
        /// </summary>
        private void MaintainBuffers()
        {
            try
            {
                while (this.buffering || this.bufferQueueCount > 0 || this.bufferReleaseQueue.Count > 0)
                {
                    lock (this.bufferingLock)
                    {
                        while (this.bufferReleaseQueue.Count == 0 && (this.bufferQueueCount > 0 || this.buffering))
                        {
                            Monitor.Wait(this.bufferingLock, 1000);
                        }
                    }

                    while (this.bufferReleaseQueue.Count > 0)
                    {
                        this.ProcessDone();
                    }
                }
            }
            catch (ThreadAbortException)
            {
            }
        }

        /// <summary>
        /// Frees buffers that have been used by the application.
        /// </summary>
        private void ProcessDone()
        {
            IntPtr header;

            // Pull the header data back out of unmanaged memory
            lock (this.bufferingLock)
            {
                header = this.bufferReleaseQueue.Dequeue();
                Monitor.Pulse(this.bufferingLock);
            }

            NativeMethods.WAVEHDR pwh = (NativeMethods.WAVEHDR)Marshal.PtrToStructure(header, typeof(NativeMethods.WAVEHDR));
            IntPtr data = pwh.lpData;

            NativeMethods.Throw(
                NativeMethods.waveOutUnprepareHeader(this.handle, header, (uint)Marshal.SizeOf(typeof(NativeMethods.WAVEHDR))),
                NativeMethods.ErrorSource.WaveOut);

            Marshal.FreeHGlobal(data);
            Marshal.FreeHGlobal(header);
        }

        /// <summary>
        /// Disposes of the managed and native resources used by this instance.
        /// </summary>
        /// <param name="disposing">true to dispose all resources, false to relase native resources only.</param>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Free managed resources.
            }

            if (this.handle != null)
            {
                this.Close();
            }
        }
    }
}
