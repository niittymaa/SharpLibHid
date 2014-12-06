using System;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Win32;
using System.Collections.Generic;


namespace Hid
{
    /// <summary>
    /// Represent a HID event.
    /// </summary>
    class HidEvent
    {
        public bool IsValid { get; private set; }
        public bool IsForeground { get; private set; }
        public bool IsBackground { get{return !IsForeground;} }
        public bool IsMouse { get; private set; }
        public bool IsKeyboard { get; private set; }
        public bool IsGeneric { get; private set; }

        public HidDevice Device { get; private set; }

        public ushort UsagePage { get; private set; }
        public ushort UsageCollection { get; private set; }
        public List<ushort> Usages { get; private set; }


        /// <summary>
        /// Initialize an HidEvent from a WM_INPUT message
        /// </summary>
        /// <param name="hRawInputDevice">Device Handle as provided by RAWINPUTHEADER.hDevice, typically accessed as rawinput.header.hDevice</param>
        public HidEvent(Message aMessage)
        {
            IsValid = false;
            IsKeyboard = false;
            IsGeneric = false;

            Usages = new List<ushort>();

            if (aMessage.Msg != Const.WM_INPUT)
            {
                //Has to be a WM_INPUT message
                return;
            }

            if (Macro.GET_RAWINPUT_CODE_WPARAM(aMessage.WParam) == Const.RIM_INPUT)
            {
                IsForeground = true;
            }
            else if (Macro.GET_RAWINPUT_CODE_WPARAM(aMessage.WParam) == Const.RIM_INPUTSINK)
            {
                IsForeground = false;
            }

            //Declare some pointers
            IntPtr rawInputBuffer = IntPtr.Zero;
            //My understanding is that this is basically our HID descriptor 
            IntPtr preParsedData = IntPtr.Zero;

            try
            {
                //Fetch raw input
                RAWINPUT rawInput = new RAWINPUT();
                if (!RawInput.GetRawInputData(aMessage.LParam, ref rawInput, ref rawInputBuffer))
                {
                    return;
                }

                //Fetch device info
                RID_DEVICE_INFO deviceInfo = new RID_DEVICE_INFO();
                if (!RawInput.GetDeviceInfo(rawInput.header.hDevice, ref deviceInfo))
                {
                    return;
                }

                //Get various information about this HID device
                Device = new Hid.HidDevice(rawInput.header.hDevice);                

                if (rawInput.header.dwType == Const.RIM_TYPEHID)  //Check that our raw input is HID                        
                {
                    IsGeneric = true;

                    Debug.WriteLine("WM_INPUT source device is HID.");
                    //Get Usage Page and Usage
                    //Debug.WriteLine("Usage Page: 0x" + deviceInfo.hid.usUsagePage.ToString("X4") + " Usage ID: 0x" + deviceInfo.hid.usUsage.ToString("X4"));
                    UsagePage = deviceInfo.hid.usUsagePage;
                    UsageCollection = deviceInfo.hid.usUsage;

                    preParsedData = RawInput.GetPreParsedData(rawInput.header.hDevice);

                    if (!(rawInput.hid.dwSizeHid > 1     //Make sure our HID msg size more than 1. In fact the first ushort is irrelevant to us for now
                        && rawInput.hid.dwCount > 0))    //Check that we have at least one HID msg
                    {
                        return;
                    }

                    //Allocate a buffer for one HID input
                    byte[] hidInputReport = new byte[rawInput.hid.dwSizeHid];

                    Debug.WriteLine("Raw input contains " + rawInput.hid.dwCount + " HID input report(s)");

                    //For each HID input report in our raw input
                    for (int i = 0; i < rawInput.hid.dwCount; i++)
                    {
                        //Compute the address from which to copy our HID input
                        int hidInputOffset = 0;
                        unsafe
                        {
                            byte* source = (byte*)rawInputBuffer;
                            source += sizeof(RAWINPUTHEADER) + sizeof(RAWHID) + (rawInput.hid.dwSizeHid * i);
                            hidInputOffset = (int)source;
                        }

                        //Copy HID input into our buffer
                        Marshal.Copy(new IntPtr(hidInputOffset), hidInputReport, 0, (int)rawInput.hid.dwSizeHid);

                        //Print HID input report in our debug output
                        string hidDump = "HID input report: ";
                        foreach (byte b in hidInputReport)
                        {
                            hidDump += b.ToString("X2");
                        }
                        Debug.WriteLine(hidDump);

                        //Proper parsing now
                        uint usageCount = 1; //Assuming a single usage per input report. Is that correct?
                        Win32.USAGE_AND_PAGE[] usages = new Win32.USAGE_AND_PAGE[usageCount];
                        Win32.HidStatus status = Win32.Function.HidP_GetUsagesEx(Win32.HIDP_REPORT_TYPE.HidP_Input, 0, usages, ref usageCount, preParsedData, hidInputReport, (uint)hidInputReport.Length);
                        if (status != Win32.HidStatus.HIDP_STATUS_SUCCESS)
                        {
                            Debug.WriteLine("Could not parse HID data!");
                        }
                        else
                        {
                            //Debug.WriteLine("UsagePage: 0x" + usages[0].UsagePage.ToString("X4"));
                            //Debug.WriteLine("Usage: 0x" + usages[0].Usage.ToString("X4"));
                            //Add this usage to our list
                            Usages.Add(usages[0].Usage);
                        }
                    }

                }
                else if (rawInput.header.dwType == Const.RIM_TYPEMOUSE)
                {
                    IsMouse = true;

                    Debug.WriteLine("WM_INPUT source device is Mouse.");                    
                    // do mouse handling...
                }
                else if (rawInput.header.dwType == Const.RIM_TYPEKEYBOARD)
                {
                    IsKeyboard = true;

                    Debug.WriteLine("WM_INPUT source device is Keyboard.");
                    // do keyboard handling...
                    Debug.WriteLine("Type: " + deviceInfo.keyboard.dwType.ToString());
                    Debug.WriteLine("SubType: " + deviceInfo.keyboard.dwSubType.ToString());
                    Debug.WriteLine("Mode: " + deviceInfo.keyboard.dwKeyboardMode.ToString());
                    Debug.WriteLine("Number of function keys: " + deviceInfo.keyboard.dwNumberOfFunctionKeys.ToString());
                    Debug.WriteLine("Number of indicators: " + deviceInfo.keyboard.dwNumberOfIndicators.ToString());
                    Debug.WriteLine("Number of keys total: " + deviceInfo.keyboard.dwNumberOfKeysTotal.ToString());
                }
            }
            finally
            {
                //Always executed when leaving our try block
                Marshal.FreeHGlobal(rawInputBuffer);
                Marshal.FreeHGlobal(preParsedData);
            }

            IsValid = true;
        }

        /// <summary>
        /// Print information about this device to our debug output.
        /// </summary>
        public void DebugWrite()
        {
            if (!IsValid)
            {
                Debug.WriteLine("==== Invalid HidEvent");
                return;
            }
            Device.DebugWrite();
            if (IsGeneric) Debug.WriteLine("==== Generic");
            if (IsKeyboard) Debug.WriteLine("==== Keyboard");
            if (IsMouse) Debug.WriteLine("==== Mouse");
            Debug.WriteLine("==== Foreground: " + IsForeground.ToString());
            Debug.WriteLine("==== UsagePage: 0x" + UsagePage.ToString("X4"));
            Debug.WriteLine("==== UsageCollection: 0x" + UsageCollection.ToString("X4"));
            foreach (ushort usage in Usages)
            {
                Debug.WriteLine("==== Usage: 0x" + usage.ToString("X4"));
            }
        }




    }

}