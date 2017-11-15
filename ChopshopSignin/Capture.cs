using DirectShowLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ChopshopSignin
{
    /// <summary> Summary description for MainForm. </summary>
    internal sealed class Capture : ISampleGrabberCB, IDisposable
    {
        #region Member variables

        /// <summary> graph builder interface. </summary>
        private IFilterGraph2 m_FilterGraph = null;

        // Used to snap picture on Still pin
        private IAMVideoControl m_VidControl = null;
        private IPin m_pinStill = null;

        /// <summary> so we can wait for the async job to finish </summary>
        private readonly ManualResetEvent m_PictureReady = new ManualResetEvent(false);

        private bool m_WantOne = false;

        /// <summary> Dimensions of the image, calculated once in constructor for perf. </summary>
        private int m_videoWidth;
        private int m_videoHeight;
        private int m_stride;

        /// <summary> buffer for bitmap data.  Always release by caller</summary>
        private IntPtr m_ipBuffer = IntPtr.Zero;

        // Allow you to "Connect to remote graph" from GraphEdit
        DsROTEntry m_rot = null;
        #endregion

        #region APIs
        [DllImport("Kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr Destination, IntPtr Source, [MarshalAs(UnmanagedType.U4)] int Length);
        #endregion

        // Zero based device index and device params and output window
        public Capture(int iDeviceNum, int iWidth, int iHeight, short iBPP)
        {
            // Get the collection of video devices
            var capDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

            if (iDeviceNum + 1 > capDevices.Length)
                throw new Exception("No video capture devices found at that index!");

            try
            {
                // Set up the capture graph
                SetupGraph(capDevices[iDeviceNum], iWidth, iHeight, iBPP);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary> release everything. </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                if (m_rot != null)
                    m_rot.Dispose();

                m_PictureReady.Dispose();
            }

            CloseInterfaces();

            GC.SuppressFinalize(this);
        }

        // Finalizer
        ~Capture()
        {
            Dispose(false);
        }

        /// <summary>
        /// Get the image from the Still pin.  The returned image can turned into a bitmap with
        /// Bitmap b = new Bitmap(cam.Width, cam.Height, cam.Stride, PixelFormat.Format24bppRgb, m_ip);
        /// If the image is upside down, you can fix it with
        /// b.RotateFlip(RotateFlipType.RotateNoneFlipY);
        /// </summary>
        /// <returns>Returned pointer to be freed by caller with Marshal.FreeCoTaskMem</returns>
        public IntPtr Click()
        {
            // get ready to wait for new image
            m_PictureReady.Reset();
            m_ipBuffer = Marshal.AllocCoTaskMem(Math.Abs(m_stride) * m_videoHeight);
            m_WantOne = true;

            try
            {
                // If we are using a still pin, ask for a picture
                if (m_VidControl != null)
                {
                    // Tell the camera to send an image
                    VideoControlFlags flags;
                    m_VidControl.GetMode(m_pinStill, out flags);
                    if ((flags & VideoControlFlags.Trigger) == VideoControlFlags.Trigger)
                    {
                        var hr = m_VidControl.SetMode(m_pinStill, VideoControlFlags.Trigger);
                        DsError.ThrowExceptionForHR(hr);
                    }
                }

                // Start waiting
                if (!m_PictureReady.WaitOne(9000, false))
                    throw new Exception("Timeout waiting to get picture");
            }
            catch
            {
                Marshal.FreeCoTaskMem(m_ipBuffer);
                m_ipBuffer = IntPtr.Zero;
                throw;
            }

            // Got one
            return m_ipBuffer;
        }

        public int Width { get { return m_videoWidth; } }
        public int Height { get { return m_videoHeight; } }
        public int Stride { get { return m_stride; } }


        /// <summary> build the capture graph for grabber. </summary>
        private void SetupGraph(DsDevice dev, int iWidth, int iHeight, short iBPP)
        {
            ISampleGrabber sampGrabber = null;
            IBaseFilter capFilter = null;
            IPin pCaptureOut = null;
            IPin pSampleIn = null;
            IPin pRenderIn = null;

            // Get the graphbuilder object
            m_FilterGraph = new FilterGraph() as IFilterGraph2;

            try
            {
#if DEBUG
                m_rot = new DsROTEntry(m_FilterGraph);
#endif
                // add the video input device
                var hr = m_FilterGraph.AddSourceFilterForMoniker(dev.Mon, null, dev.Name, out capFilter);
                DsError.ThrowExceptionForHR(hr);

                // Didn't find one.  Is there a preview pin?
                if (m_pinStill == null)
                    m_pinStill = DsFindPin.ByCategory(capFilter, PinCategory.Preview, 0);

                // Still haven't found one.  Need to put a splitter in so we have
                // one stream to capture the bitmap from, and one to display.  Ok, we
                // don't *have* to do it that way, but we are going to anyway.
                if (m_pinStill == null)
                {
                    IPin pRaw = null;
                    IPin pSmart = null;

                    // There is no still pin
                    m_VidControl = null;

                    // Add a splitter
                    var iSmartTee = (IBaseFilter)new SmartTee();

                    try
                    {
                        hr = m_FilterGraph.AddFilter(iSmartTee, "SmartTee");
                        DsError.ThrowExceptionForHR(hr);

                        // Find the find the capture pin from the video device and the
                        // input pin for the splitter, and connnect them
                        pRaw = DsFindPin.ByCategory(capFilter, PinCategory.Capture, 0);
                        pSmart = DsFindPin.ByDirection(iSmartTee, PinDirection.Input, 0);

                        hr = m_FilterGraph.Connect(pRaw, pSmart);
                        DsError.ThrowExceptionForHR(hr);

                        // Now set the capture and still pins (from the splitter)
                        m_pinStill = DsFindPin.ByName(iSmartTee, "Preview");
                        pCaptureOut = DsFindPin.ByName(iSmartTee, "Capture");

                        // If any of the default config items are set, perform the config
                        // on the actual video device (rather than the splitter)
                        if (iHeight + iWidth + iBPP > 0)
                        {
                            SetConfigParms(pRaw, iWidth, iHeight, iBPP);
                        }
                    }
                    finally
                    {
                        if (pRaw != null)
                            Marshal.ReleaseComObject(pRaw);

                        if (pRaw != pSmart)
                            Marshal.ReleaseComObject(pSmart);

                        if (pRaw != iSmartTee)
                            Marshal.ReleaseComObject(iSmartTee);
                    }
                }
                else
                {
                    // Get a control pointer (used in Click())
                    m_VidControl = capFilter as IAMVideoControl;

                    pCaptureOut = DsFindPin.ByCategory(capFilter, PinCategory.Capture, 0);

                    // If any of the default config items are set
                    if (iHeight + iWidth + iBPP > 0)
                    {
                        SetConfigParms(m_pinStill, iWidth, iHeight, iBPP);
                    }
                }

                // Get the SampleGrabber interface
                sampGrabber = (ISampleGrabber)new SampleGrabber();

                // Configure the sample grabber
                IBaseFilter baseGrabFlt = (IBaseFilter)sampGrabber;
                ConfigureSampleGrabber(sampGrabber);
                pSampleIn = DsFindPin.ByDirection(baseGrabFlt, PinDirection.Input, 0);

                // Get the default video renderer
                var pRenderer = (IBaseFilter)new VideoRendererDefault();
                hr = m_FilterGraph.AddFilter(pRenderer, "Renderer");
                DsError.ThrowExceptionForHR(hr);

                pRenderIn = DsFindPin.ByDirection(pRenderer, PinDirection.Input, 0);

                // Add the sample grabber to the graph
                hr = m_FilterGraph.AddFilter(baseGrabFlt, "DS.NET Grabber");
                DsError.ThrowExceptionForHR(hr);

                if (m_VidControl == null)
                {
                    // Connect the Still pin to the sample grabber
                    hr = m_FilterGraph.Connect(m_pinStill, pSampleIn);
                    DsError.ThrowExceptionForHR(hr);

                    // Connect the capture pin to the renderer
                    hr = m_FilterGraph.Connect(pCaptureOut, pRenderIn);
                    DsError.ThrowExceptionForHR(hr);
                }
                else
                {
                    // Connect the capture pin to the renderer
                    hr = m_FilterGraph.Connect(pCaptureOut, pRenderIn);
                    DsError.ThrowExceptionForHR(hr);

                    // Connect the Still pin to the sample grabber
                    hr = m_FilterGraph.Connect(m_pinStill, pSampleIn);
                    DsError.ThrowExceptionForHR(hr);
                }

                // Learn the video properties
                SaveSizeInfo(sampGrabber);

                // Start the graph
                var mediaCtrl = (IMediaControl)m_FilterGraph;
                hr = mediaCtrl.Run();
                DsError.ThrowExceptionForHR(hr);
            }
            finally
            {
                if (sampGrabber != null)
                    Marshal.ReleaseComObject(sampGrabber);

                if (pCaptureOut != null)
                    Marshal.ReleaseComObject(pCaptureOut);

                if (pRenderIn != null)
                    Marshal.ReleaseComObject(pRenderIn);

                if (pSampleIn != null)
                    Marshal.ReleaseComObject(pSampleIn);
            }
        }

        private void SaveSizeInfo(ISampleGrabber sampGrabber)
        {
            // Get the media type from the SampleGrabber
            var media = new AMMediaType();

            var hr = sampGrabber.GetConnectedMediaType(media);
            DsError.ThrowExceptionForHR(hr);

            if ((media.formatType != FormatType.VideoInfo) || (media.formatPtr == IntPtr.Zero))
            {
                throw new NotSupportedException("Unknown Grabber Media Format");
            }

            // Grab the size info
            var videoInfoHeader = (VideoInfoHeader)Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader));
            m_videoWidth = videoInfoHeader.BmiHeader.Width;
            m_videoHeight = videoInfoHeader.BmiHeader.Height;
            m_stride = m_videoWidth * (videoInfoHeader.BmiHeader.BitCount / 8);

            DsUtils.FreeAMMediaType(media);
        }

        private void ConfigureSampleGrabber(ISampleGrabber sampGrabber)
        {
            var media = new AMMediaType();

            // Set the media type to Video/RBG24
            media.majorType = MediaType.Video;
            media.subType = MediaSubType.RGB24;
            media.formatType = FormatType.VideoInfo;
            var hr = sampGrabber.SetMediaType(media);
            DsError.ThrowExceptionForHR(hr);

            DsUtils.FreeAMMediaType(media);

            // Configure the samplegrabber
            hr = sampGrabber.SetCallback(this, 1);
            DsError.ThrowExceptionForHR(hr);
        }

        // Set the Framerate, and video size
        private void SetConfigParms(IPin pStill, int iWidth, int iHeight, short iBPP)
        {
            AMMediaType media = null;

            try
            {
                var videoStreamConfig = (IAMStreamConfig)pStill;

                // Get the existing format block
                var hr = videoStreamConfig.GetFormat(out media);
                DsError.ThrowExceptionForHR(hr);

                // copy out the videoinfoheader
                var v = new VideoInfoHeader();
                Marshal.PtrToStructure(media.formatPtr, v);

                // if overriding the width, set the width
                if (iWidth > 0)
                    v.BmiHeader.Width = iWidth;

                // if overriding the Height, set the Height
                if (iHeight > 0)
                    v.BmiHeader.Height = iHeight;

                // if overriding the bits per pixel
                if (iBPP > 0)
                    v.BmiHeader.BitCount = iBPP;

                // Copy the media structure back
                Marshal.StructureToPtr(v, media.formatPtr, false);

                // Set the new format
                hr = videoStreamConfig.SetFormat(media);
                DsError.ThrowExceptionForHR(hr);
            }
            finally
            {
                if (media != null)
                    DsUtils.FreeAMMediaType(media);
            }
        }

        /// <summary> Shut down capture </summary>
        private void CloseInterfaces()
        {
            try
            {
                if (m_FilterGraph != null)
                    // Stop the graph
                    ((IMediaControl)m_FilterGraph).Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }

            if (m_FilterGraph != null)
                Marshal.ReleaseComObject(m_FilterGraph);

            if (m_VidControl != null)
                Marshal.ReleaseComObject(m_VidControl);

            if (m_pinStill != null)
                Marshal.ReleaseComObject(m_pinStill);
        }

        /// <summary> sample callback, NOT USED. </summary>
        int ISampleGrabberCB.SampleCB(double SampleTime, IMediaSample pSample)
        {
            Marshal.ReleaseComObject(pSample);
            return 0;
        }

        /// <summary> buffer callback, COULD BE FROM FOREIGN THREAD. </summary>
        int ISampleGrabberCB.BufferCB(double SampleTime, IntPtr pBuffer, int BufferLen)
        {
            // Note that we depend on only being called once per call to Click. Otherwise
            // a second call can overwrite the previous image.
            Debug.Assert(BufferLen == Math.Abs(m_stride) * m_videoHeight, "Incorrect buffer length");

            if (m_WantOne)
            {
                m_WantOne = false;
                Debug.Assert(m_ipBuffer != IntPtr.Zero, "Unitialized buffer");

                // Save the buffer
                CopyMemory(m_ipBuffer, pBuffer, BufferLen);

                // Picture is ready.
                m_PictureReady.Set();
            }

            return 0;
        }
    }
}
