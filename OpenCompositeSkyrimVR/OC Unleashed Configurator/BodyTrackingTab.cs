using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace OpenCompositeConfigurator
{
    // ═══════════════════════════════════════════════════════════════════════
    // BODY TRACKING TAB
    // Webcam / phone camera -> pose AI (ONNX, DirectML) -> OSC trackers into
    // the OCU DLL's network tracker receiver (127.0.0.1, networkTrackerPort).
    // Left: body silhouette with tracker points lighting up as they track
    // (KAT treadmill shows as a puck under the feet). Right: live video with
    // the detected skeleton drawn over it.
    //
    // The pose model is a single-person COCO-17 keypoint ONNX (MoveNet-style,
    // input [1,H,W,3], output [1,1,17,3] y/x/score) at:
    //   <exe>\BodyTracking\pose_model.onnx
    // Without it the tab still previews video; dots stay grey.
    // ═══════════════════════════════════════════════════════════════════════
    public partial class MainForm
    {
        private Button _btnTabBody = null!;
        private Panel _tabBody = null!;

        private CheckBox _chkNetTrackersEnabled = null!;
        private ComboBox _cmbBodyCamera = null!;
        private TextBox _txtBodyCamUrl = null!;
        private Button _btnBodyStartStop = null!;
        private CheckBox _chkBodyMirror = null!;
        private CheckBox _chkBodyStream = null!;
        private NumericUpDown _nudBodyHeightCm = null!;
        private Label _lblBodyStatus = null!;
        private PictureBox _picBodyVideo = null!;
        private BodySilhouettePanel _pnlBodySilhouette = null!;

        private Thread? _bodyCapThread;
        private volatile bool _bodyCapRunning;
        private UdpClient? _bodyOsc;
        private int _bodyOscPort = 9000;

        // Latest keypoints (normalized 0..1 image coords + confidence),
        // COCO-17 order. Written by the capture thread, read by the UI.
        private readonly float[,] _bodyKp = new float[17, 3];
        private readonly object _bodyKpLock = new object();
        private volatile bool _bodyPoseValid;
        private volatile bool _bodyKatDetected;

        private const string FbtModUrl = "https://www.nexusmods.com/skyrimspecialedition/mods/185070";

        // COCO-17 indices
        private const int KpNose = 0, KpLSho = 5, KpRSho = 6, KpLElb = 7, KpRElb = 8,
            KpLWri = 9, KpRWri = 10, KpLHip = 11, KpRHip = 12, KpLKnee = 13, KpRKnee = 14,
            KpLAnk = 15, KpRAnk = 16;

        private static readonly (int a, int b)[] SkeletonBones =
        {
            (KpLSho, KpRSho), (KpLSho, KpLElb), (KpLElb, KpLWri),
            (KpRSho, KpRElb), (KpRElb, KpRWri),
            (KpLSho, KpLHip), (KpRSho, KpRHip), (KpLHip, KpRHip),
            (KpLHip, KpLKnee), (KpLKnee, KpLAnk),
            (KpRHip, KpRKnee), (KpRKnee, KpRAnk),
        };

        private void BuildBodyTrackingTab()
        {
            var container = _tabBody;
            int leftMargin = 6;
            int rightEdge = container.ClientSize.Width - 20;
            int y = 10;

            var lblIntro = new Label
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 24),
                Text = "Camera-based full body tracking: your webcam or phone becomes body trackers for Skyrim VR. No tracker hardware needed.",
                ForeColor = Color.FromArgb(150, 200, 250),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                BackColor = Color.FromArgb(40, 45, 60),
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(6, 4, 6, 4),
                AutoSize = false
            };
            container.Controls.Add(lblIntro);
            y += 32;

            // ── Setup instructions ──
            var lblSteps = new Label
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 92),
                Text = "How to set up:\n"
                     + "  1.  Point a camera at your play space from 2-4 m away with your full body in frame. Webcam: pick it below. Phone: install a free IP camera app\n"
                     + "       (e.g. \"IP Webcam\" on Android), pick \"Phone / IP camera\" and paste the app's video URL (looks like http://192.168.1.x:8080/video).\n"
                     + "  2.  Set your height, press Start. The right box shows your camera with a green skeleton; the silhouette's dots turn green as each point tracks.\n"
                     + "  3.  Tick \"Send trackers to the game\" below and Save (writes networkTrackersEnabled to opencomposite.ini).\n"
                     + "  4.  Install the SkyrimVR FBT mod (link at the bottom) plus VRIK, HIGGS and PLANCK.\n"
                     + "  5.  Keep this window running, launch the game, then run the FBT mod's calibration. Your hips and legs now follow your real body.",
                ForeColor = Color.FromArgb(190, 190, 195),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false
            };
            container.Controls.Add(lblSteps);
            y += 98;

            // ── Source row ──
            container.Controls.Add(MakeLabel("Camera:", leftMargin, y + 3, 60));
            _cmbBodyCamera = new ComboBox
            {
                Location = new Point(leftMargin + 60, y), Width = 170,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            _cmbBodyCamera.Items.AddRange(new object[]
                { "Webcam 0", "Webcam 1", "Webcam 2", "Webcam 3", "Phone / IP camera (URL)" });
            _cmbBodyCamera.SelectedIndex = 0;
            _cmbBodyCamera.SelectedIndexChanged += (s, e) =>
                _txtBodyCamUrl.Enabled = _cmbBodyCamera.SelectedIndex == 4;
            container.Controls.Add(_cmbBodyCamera);

            _txtBodyCamUrl = new TextBox
            {
                Location = new Point(leftMargin + 238, y), Width = 260,
                Text = "http://192.168.1.100:8080/video",
                Enabled = false,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            container.Controls.Add(_txtBodyCamUrl);

            _btnBodyStartStop = new Button
            {
                Location = new Point(leftMargin + 508, y - 2), Size = new Size(90, 26),
                Text = "Start",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.White, BackColor = Color.FromArgb(40, 120, 40), // match the Save button green
                Cursor = Cursors.Hand,
            };
            _btnBodyStartStop.Click += (s, e) => ToggleBodyCapture();
            container.Controls.Add(_btnBodyStartStop);
            y += 30;

            // ── Options row ──
            _chkBodyMirror = MakeCheckBox("Mirror", leftMargin, y);
            _chkBodyMirror.Checked = true;
            container.Controls.Add(_chkBodyMirror);

            _chkBodyStream = MakeCheckBox("Stream to OCU (OSC)", leftMargin + 80, y);
            _chkBodyStream.Checked = true;
            container.Controls.Add(_chkBodyStream);

            container.Controls.Add(MakeLabel("Your height:", leftMargin + 260, y + 2, 80));
            _nudBodyHeightCm = new NumericUpDown
            {
                Location = new Point(leftMargin + 340, y), Width = 55,
                Minimum = 120, Maximum = 220, Value = 175,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            container.Controls.Add(_nudBodyHeightCm);
            container.Controls.Add(MakeLabel("cm", leftMargin + 398, y + 2, 25));

            _lblBodyStatus = MakeLabel("Idle", leftMargin + 440, y + 2, rightEdge - leftMargin - 440);
            _lblBodyStatus.ForeColor = Color.FromArgb(160, 160, 160);
            container.Controls.Add(_lblBodyStatus);
            y += 26;

            _chkNetTrackersEnabled = MakeCheckBox("Send trackers to the game (networkTrackersEnabled, needs game restart)", leftMargin, y);
            container.Controls.Add(_chkNetTrackersEnabled);
            y += 26;

            // ── Left: silhouette / Right: video ──
            int panelH = 380;
            int silW = 240;
            _pnlBodySilhouette = new BodySilhouettePanel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(silW, panelH),
                BackColor = Color.FromArgb(24, 24, 28),
                BorderStyle = BorderStyle.FixedSingle,
            };
            container.Controls.Add(_pnlBodySilhouette);

            _picBodyVideo = new PictureBox
            {
                Location = new Point(leftMargin + silW + 8, y),
                Size = new Size(rightEdge - leftMargin - silW - 8, panelH),
                BackColor = Color.Black,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
            };
            container.Controls.Add(_picBodyVideo);
            y += panelH + 8;

            // ── FBT mod callout ──
            var pnlFbt = new Panel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 46),
                BackColor = Color.FromArgb(45, 55, 40),
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lnkFbt = new LinkLabel
            {
                Location = new Point(8, 4),
                Size = new Size(pnlFbt.Width - 16, 18),
                Text = "Required in-game mod: SkyrimVR FBT - Full Body Tracking with Physics (Nexus) — click to open",
                LinkColor = Color.FromArgb(140, 210, 140),
                ActiveLinkColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            lnkFbt.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(FbtModUrl) { UseShellExecute = true }); }
                catch { }
            };
            pnlFbt.Controls.Add(lnkFbt);
            var lblFbtNote = new Label
            {
                Location = new Point(8, 23),
                Size = new Size(pnlFbt.Width - 16, 18),
                Text = "Needs VRIK + HIGGS + PLANCK. Without it, trackers exist but nothing in-game moves.",
                ForeColor = Color.FromArgb(150, 150, 150),
                Font = new Font("Segoe UI", 8f),
            };
            pnlFbt.Controls.Add(lblFbtNote);
            container.Controls.Add(pnlFbt);
            y += 54;

            container.Size = new Size(container.Width, y + 6);

            // Silhouette repaint + KAT presence poll while the tab is alive
            var uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            int katPollCounter = 0;
            uiTimer.Tick += (s, e) =>
            {
                if (!_tabBody.Visible) return;
                if (++katPollCounter >= 50) // every 5s
                {
                    katPollCounter = 0;
                    try
                    {
                        _bodyKatDetected =
                            System.Diagnostics.Process.GetProcessesByName("KAT Gateway").Length > 0
                            || System.Diagnostics.Process.GetProcessesByName("KATGateway").Length > 0
                            || System.Diagnostics.Process.GetProcessesByName("KAT Gateway Core").Length > 0;
                    }
                    catch { _bodyKatDetected = false; }
                }
                lock (_bodyKpLock)
                {
                    _pnlBodySilhouette.UpdateState(_bodyKp, _bodyPoseValid, _bodyKatDetected);
                }
                _pnlBodySilhouette.Invalidate();
            };
            uiTimer.Start();

            FormClosing += (s, e) => StopBodyCapture();
        }

        private void ToggleBodyCapture()
        {
            if (_bodyCapRunning) { StopBodyCapture(); return; }

            _bodyOscPort = 9000;
            if (int.TryParse(_ini.Get("input", "networkTrackerPort", _ini.Get("", "networkTrackerPort", "9000")), out int p))
                _bodyOscPort = p;

            int camIndex = _cmbBodyCamera.SelectedIndex;
            string source = camIndex == 4 ? _txtBodyCamUrl.Text.Trim() : camIndex.ToString();

            _bodyCapRunning = true;
            _btnBodyStartStop.Text = "Stop";
            _btnBodyStartStop.BackColor = Color.FromArgb(150, 45, 45);
            _lblBodyStatus.Text = "Opening camera...";
            _lblBodyStatus.ForeColor = Color.FromArgb(220, 200, 120);

            _bodyCapThread = new Thread(() => BodyCaptureLoop(source)) { IsBackground = true };
            _bodyCapThread.Start();
        }

        private void StopBodyCapture()
        {
            _bodyCapRunning = false;
            try { _bodyCapThread?.Join(1500); } catch { }
            _bodyCapThread = null;
            try { _bodyOsc?.Dispose(); } catch { }
            _bodyOsc = null;
            _bodyPoseValid = false;
            if (_btnBodyStartStop != null && !IsDisposed)
            {
                _btnBodyStartStop.Text = "Start";
                _btnBodyStartStop.BackColor = Color.FromArgb(40, 120, 40);
                _lblBodyStatus.Text = "Idle";
                _lblBodyStatus.ForeColor = Color.FromArgb(160, 160, 160);
            }
        }

        private void BodySetStatus(string text, Color color)
        {
            try
            {
                BeginInvoke(() => { _lblBodyStatus.Text = text; _lblBodyStatus.ForeColor = color; });
            }
            catch { }
        }

        // ── Capture worker ────────────────────────────────────────────────

        private void BodyCaptureLoop(string source)
        {
            OpenCvSharp.VideoCapture? cap = null;
            InferenceSession? pose = null;
            string poseInputName = "";
            int poseW = 256, poseH = 256;
            bool poseWantsInt = true;
            bool poseNchw = false; // PINTO MoveNet exports are NCHW; TF-style are NHWC

            try
            {
                cap = int.TryParse(source, out int idx)
                    ? new OpenCvSharp.VideoCapture(idx, OpenCvSharp.VideoCaptureAPIs.DSHOW)
                    : new OpenCvSharp.VideoCapture(source);
                if (!cap.IsOpened())
                {
                    BodySetStatus("Camera failed to open: " + source, Color.FromArgb(255, 120, 120));
                    _bodyCapRunning = false;
                    BeginInvoke(() => StopBodyCapture());
                    return;
                }

                // Pose model (optional: video-only preview without it)
                string modelPath = Path.Combine(AppContext.BaseDirectory, "BodyTracking", "pose_model.onnx");
                if (File.Exists(modelPath))
                {
                    try
                    {
                        var opts = new SessionOptions();
                        try { opts.AppendExecutionProvider_DML(0); } catch { /* CPU fallback */ }
                        pose = new InferenceSession(modelPath, opts);
                        var input = pose.InputMetadata.First();
                        poseInputName = input.Key;
                        var dims = input.Value.Dimensions;
                        if (dims.Length == 4)
                        {
                            if (dims[1] == 3) // [1,3,H,W]
                            {
                                poseNchw = true;
                                poseH = dims[2] > 0 ? dims[2] : 256;
                                poseW = dims[3] > 0 ? dims[3] : 256;
                            }
                            else // [1,H,W,3]
                            {
                                poseH = dims[1] > 0 ? dims[1] : 256;
                                poseW = dims[2] > 0 ? dims[2] : 256;
                            }
                        }
                        poseWantsInt = input.Value.ElementType == typeof(int);
                        BodySetStatus($"Tracking ({poseW}x{poseH} model)", Color.FromArgb(120, 220, 120));
                    }
                    catch (Exception ex)
                    {
                        pose = null;
                        BodySetStatus("Pose model failed to load: " + ex.Message, Color.FromArgb(255, 120, 120));
                    }
                }
                else
                {
                    BodySetStatus("Video only — no pose model (BodyTracking\\pose_model.onnx)", Color.FromArgb(220, 200, 120));
                }

                _bodyOsc = new UdpClient();
                _bodyOsc.Connect(IPAddress.Loopback, _bodyOscPort);

                using var frame = new OpenCvSharp.Mat();
                using var resized = new OpenCvSharp.Mat();
                var kp = new float[17, 3];

                while (_bodyCapRunning)
                {
                    if (!cap.Read(frame) || frame.Empty()) { Thread.Sleep(30); continue; }
                    if (_chkBodyMirror.Checked)
                        OpenCvSharp.Cv2.Flip(frame, frame, OpenCvSharp.FlipMode.Y);

                    bool poseOk = false;
                    if (pose != null)
                    {
                        try
                        {
                            OpenCvSharp.Cv2.Resize(frame, resized, new OpenCvSharp.Size(poseW, poseH));
                            OpenCvSharp.Cv2.CvtColor(resized, resized, OpenCvSharp.ColorConversionCodes.BGR2RGB);
                            var bytes = new byte[poseW * poseH * 3];
                            Marshal.Copy(resized.Data, bytes, 0, bytes.Length);

                            // Values stay raw 0-255 in both layouts (MoveNet
                            // normalizes internally; /255 collapses confidence).
                            int hw = poseH * poseW;
                            NamedOnnxValue inputValue;
                            if (poseWantsInt)
                            {
                                var t = new DenseTensor<int>(poseNchw ? new[] { 1, 3, poseH, poseW } : new[] { 1, poseH, poseW, 3 });
                                if (poseNchw)
                                    for (int c = 0; c < 3; c++)
                                        for (int i = 0; i < hw; i++) t.Buffer.Span[c * hw + i] = bytes[i * 3 + c];
                                else
                                    for (int i = 0; i < bytes.Length; i++) t.Buffer.Span[i] = bytes[i];
                                inputValue = NamedOnnxValue.CreateFromTensor(poseInputName, t);
                            }
                            else
                            {
                                var t = new DenseTensor<float>(poseNchw ? new[] { 1, 3, poseH, poseW } : new[] { 1, poseH, poseW, 3 });
                                if (poseNchw)
                                    for (int c = 0; c < 3; c++)
                                        for (int i = 0; i < hw; i++) t.Buffer.Span[c * hw + i] = bytes[i * 3 + c];
                                else
                                    for (int i = 0; i < bytes.Length; i++) t.Buffer.Span[i] = bytes[i];
                                inputValue = NamedOnnxValue.CreateFromTensor(poseInputName, t);
                            }

                            using var results = pose.Run(new[] { inputValue });
                            var outT = results.First().AsEnumerable<float>().ToArray();
                            if (outT.Length >= 17 * 3)
                            {
                                for (int i = 0; i < 17; i++)
                                {
                                    kp[i, 1] = outT[i * 3 + 0]; // y
                                    kp[i, 0] = outT[i * 3 + 1]; // x
                                    kp[i, 2] = outT[i * 3 + 2]; // confidence
                                }
                                poseOk = true;
                            }
                        }
                        catch { poseOk = false; }
                    }

                    if (poseOk)
                    {
                        lock (_bodyKpLock)
                        {
                            Array.Copy(kp, _bodyKp, kp.Length);
                            _bodyPoseValid = true;
                        }
                        if (_chkBodyStream.Checked)
                            SendBodyOsc(kp);
                    }

                    // Preview with skeleton overlay
                    var bmp = MatToBitmap(frame);
                    if (poseOk)
                        DrawSkeleton(bmp, kp);
                    try
                    {
                        BeginInvoke(() =>
                        {
                            var old = _picBodyVideo.Image;
                            _picBodyVideo.Image = bmp;
                            old?.Dispose();
                        });
                    }
                    catch { bmp.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                BodySetStatus("Capture error: " + ex.Message, Color.FromArgb(255, 120, 120));
            }
            finally
            {
                try { cap?.Release(); cap?.Dispose(); } catch { }
                try { pose?.Dispose(); } catch { }
                _bodyPoseValid = false;
            }
        }

        private static Bitmap MatToBitmap(OpenCvSharp.Mat mat)
        {
            int w = mat.Width, h = mat.Height;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            try
            {
                long srcStep = mat.Step();
                int rowBytes = w * 3;
                var row = new byte[rowBytes];
                for (int r = 0; r < h; r++)
                {
                    Marshal.Copy(mat.Data + (nint)(r * srcStep), row, 0, rowBytes);
                    Marshal.Copy(row, 0, data.Scan0 + r * data.Stride, rowBytes);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        private static void DrawSkeleton(Bitmap bmp, float[,] kp)
        {
            const float minConf = 0.3f;
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bonePen = new Pen(Color.FromArgb(220, 90, 220, 120), 3f);
            using var dotBrush = new SolidBrush(Color.FromArgb(255, 250, 200, 60));

            foreach (var (a, b) in SkeletonBones)
            {
                if (kp[a, 2] < minConf || kp[b, 2] < minConf) continue;
                g.DrawLine(bonePen,
                    kp[a, 0] * bmp.Width, kp[a, 1] * bmp.Height,
                    kp[b, 0] * bmp.Width, kp[b, 1] * bmp.Height);
            }
            for (int i = 0; i < 17; i++)
            {
                if (kp[i, 2] < minConf) continue;
                float x = kp[i, 0] * bmp.Width, yy = kp[i, 1] * bmp.Height;
                g.FillEllipse(dotBrush, x - 4, yy - 4, 8, 8);
            }
        }

        // ── OSC sender ────────────────────────────────────────────────────
        // VRChat OSC tracker format, matching the OCU DLL's receiver:
        // /tracking/trackers/{1..8}/position + /tracking/trackers/head/position
        // Unity convention (x right, y up, meters). v1 is planar (z=0): depth
        // needs a 3D model; the DLL's head alignment absorbs the offset.

        private void SendBodyOsc(float[,] kp)
        {
            const float minConf = 0.3f;
            if (_bodyOsc == null) return;

            // meters-per-normalized-unit from user height: nose->ankle span is
            // roughly 87% of body height when standing.
            float ankY = 0;
            int ankN = 0;
            if (kp[KpLAnk, 2] > minConf) { ankY += kp[KpLAnk, 1]; ankN++; }
            if (kp[KpRAnk, 2] > minConf) { ankY += kp[KpRAnk, 1]; ankN++; }
            if (ankN == 0 || kp[KpNose, 2] < minConf) return;
            ankY /= ankN;
            float span = ankY - kp[KpNose, 1];
            if (span < 0.2f) return;
            float mPerU = ((float)_nudBodyHeightCm.Value / 100f) * 0.87f / span;

            void Send(string addr, float nx, float ny)
            {
                float ux = (nx - 0.5f) * mPerU;
                float uy = (ankY - ny) * mPerU; // ankles ~ floor
                SendOscVec(addr, ux, uy, 0f);
            }

            // Head (alignment reference for the DLL)
            Send("/tracking/trackers/head/position", kp[KpNose, 0], kp[KpNose, 1]);

            // Hip = mid of both hips -> slot 1
            if (kp[KpLHip, 2] > minConf && kp[KpRHip, 2] > minConf)
                Send("/tracking/trackers/1/position",
                    (kp[KpLHip, 0] + kp[KpRHip, 0]) / 2f, (kp[KpLHip, 1] + kp[KpRHip, 1]) / 2f);
            if (kp[KpLAnk, 2] > minConf)
                Send("/tracking/trackers/2/position", kp[KpLAnk, 0], kp[KpLAnk, 1]);
            if (kp[KpRAnk, 2] > minConf)
                Send("/tracking/trackers/3/position", kp[KpRAnk, 0], kp[KpRAnk, 1]);
            if (kp[KpLKnee, 2] > minConf)
                Send("/tracking/trackers/4/position", kp[KpLKnee, 0], kp[KpLKnee, 1]);
            if (kp[KpRKnee, 2] > minConf)
                Send("/tracking/trackers/5/position", kp[KpRKnee, 0], kp[KpRKnee, 1]);
            if (kp[KpLElb, 2] > minConf)
                Send("/tracking/trackers/6/position", kp[KpLElb, 0], kp[KpLElb, 1]);
            if (kp[KpRElb, 2] > minConf)
                Send("/tracking/trackers/7/position", kp[KpRElb, 0], kp[KpRElb, 1]);
            if (kp[KpLSho, 2] > minConf && kp[KpRSho, 2] > minConf)
                Send("/tracking/trackers/8/position",
                    (kp[KpLSho, 0] + kp[KpRSho, 0]) / 2f, (kp[KpLSho, 1] + kp[KpRSho, 1]) / 2f); // chest
        }

        private void SendOscVec(string address, float x, float y, float z)
        {
            try
            {
                var ms = new MemoryStream();
                void PadString(string s)
                {
                    var b = System.Text.Encoding.ASCII.GetBytes(s);
                    ms.Write(b, 0, b.Length);
                    int pad = 4 - (b.Length % 4);
                    for (int i = 0; i < pad; i++) ms.WriteByte(0);
                }
                void BigFloat(float f)
                {
                    var b = BitConverter.GetBytes(f);
                    Array.Reverse(b);
                    ms.Write(b, 0, 4);
                }
                PadString(address);
                PadString(",fff");
                BigFloat(x); BigFloat(y); BigFloat(z);
                var pkt = ms.ToArray();
                _bodyOsc?.Send(pkt, pkt.Length);
            }
            catch { }
        }
    }

    // ── Silhouette panel ─────────────────────────────────────────────────
    // Simple standing human outline; tracker dots light green when the
    // matching keypoint tracks. KAT treadmill = puck under the feet.
    internal class BodySilhouettePanel : Panel
    {
        private float[,] _kp = new float[17, 3];
        private bool _valid;
        private bool _kat;

        public BodySilhouettePanel() { DoubleBuffered = true; }

        public void UpdateState(float[,] kp, bool valid, bool kat)
        {
            Array.Copy(kp, _kp, kp.Length);
            _valid = valid;
            _kat = kat;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float w = ClientSize.Width, h = ClientSize.Height;
            float cx = w / 2f;
            // Body proportions inside the panel
            float top = h * 0.06f, bottom = h * 0.86f;
            float bodyH = bottom - top;
            float headR = bodyH * 0.07f;

            using var outlinePen = new Pen(Color.FromArgb(90, 90, 100), 2.5f);

            float headCy = top + headR;
            float neckY = headCy + headR;
            float shoY = neckY + bodyH * 0.04f;
            float hipY = top + bodyH * 0.47f;
            float kneeY = top + bodyH * 0.72f;
            float ankY = bottom;
            float shoHalf = bodyH * 0.13f;
            float hipHalf = bodyH * 0.075f;
            float elbY = shoY + bodyH * 0.17f;
            float wriY = shoY + bodyH * 0.33f;
            float armX = shoHalf + bodyH * 0.05f;

            // Outline
            g.DrawEllipse(outlinePen, cx - headR, headCy - headR, headR * 2, headR * 2);
            g.DrawLine(outlinePen, cx, neckY, cx, hipY);                      // spine
            g.DrawLine(outlinePen, cx - shoHalf, shoY, cx + shoHalf, shoY);   // shoulders
            g.DrawLine(outlinePen, cx - shoHalf, shoY, cx - armX, elbY);      // upper arms
            g.DrawLine(outlinePen, cx + shoHalf, shoY, cx + armX, elbY);
            g.DrawLine(outlinePen, cx - armX, elbY, cx - armX, wriY);         // forearms
            g.DrawLine(outlinePen, cx + armX, elbY, cx + armX, wriY);
            g.DrawLine(outlinePen, cx - hipHalf, hipY, cx + hipHalf, hipY);   // hips
            g.DrawLine(outlinePen, cx - hipHalf, hipY, cx - hipHalf, kneeY);  // thighs
            g.DrawLine(outlinePen, cx + hipHalf, hipY, cx + hipHalf, kneeY);
            g.DrawLine(outlinePen, cx - hipHalf, kneeY, cx - hipHalf, ankY);  // shins
            g.DrawLine(outlinePen, cx + hipHalf, kneeY, cx + hipHalf, ankY);

            // KAT treadmill puck under the feet
            if (_kat)
            {
                using var puckBrush = new SolidBrush(Color.FromArgb(200, 180, 140, 40));
                using var puckPen = new Pen(Color.FromArgb(240, 210, 160, 60), 2f);
                g.FillEllipse(puckBrush, cx - w * 0.30f, ankY + 4, w * 0.60f, h * 0.055f);
                g.DrawEllipse(puckPen, cx - w * 0.30f, ankY + 4, w * 0.60f, h * 0.055f);
                DrawCenteredText(g, "KAT treadmill", cx, ankY + h * 0.055f + 8, Color.FromArgb(210, 170, 70));
            }
            else
            {
                DrawCenteredText(g, "no treadmill", cx, ankY + 10, Color.FromArgb(70, 70, 78));
            }

            // Tracker dots: (label position, keypoint index or -1 for derived)
            const float minConf = 0.3f;
            bool KpOk(int i) => _valid && _kp[i, 2] > minConf;
            void Dot(float x, float y, bool on)
            {
                using var b = new SolidBrush(on ? Color.FromArgb(90, 230, 90) : Color.FromArgb(70, 70, 78));
                g.FillEllipse(b, x - 5, y - 5, 10, 10);
                if (on)
                {
                    using var glow = new Pen(Color.FromArgb(90, 90, 230, 90), 4f);
                    g.DrawEllipse(glow, x - 7, y - 7, 14, 14);
                }
            }

            Dot(cx, headCy, KpOk(0));                                          // head
            Dot(cx, (shoY + hipY) / 2f, KpOk(5) && KpOk(6));                   // chest
            Dot(cx, hipY, KpOk(11) && KpOk(12));                               // waist
            Dot(cx - armX, elbY, KpOk(7));                                     // elbows
            Dot(cx + armX, elbY, KpOk(8));
            Dot(cx - armX, wriY, KpOk(9));                                     // wrists
            Dot(cx + armX, wriY, KpOk(10));
            Dot(cx - hipHalf, kneeY, KpOk(13));                                // knees
            Dot(cx + hipHalf, kneeY, KpOk(14));
            Dot(cx - hipHalf, ankY, KpOk(15));                                 // ankles/feet
            Dot(cx + hipHalf, ankY, KpOk(16));

            DrawCenteredText(g, _valid ? "TRACKING" : "no pose", cx, top - 2, _valid ? Color.FromArgb(90, 230, 90) : Color.FromArgb(110, 110, 120));
        }

        private static void DrawCenteredText(Graphics g, string text, float cx, float y, Color color)
        {
            using var f = new Font("Segoe UI", 8f, FontStyle.Bold);
            var sz = g.MeasureString(text, f);
            using var b = new SolidBrush(color);
            g.DrawString(text, f, b, cx - sz.Width / 2f, y);
        }
    }
}
