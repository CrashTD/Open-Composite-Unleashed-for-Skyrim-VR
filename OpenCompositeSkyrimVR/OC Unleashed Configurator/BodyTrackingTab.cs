using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using OpenCompositeConfigurator.BodyTracking;

namespace OpenCompositeConfigurator
{
    // ═══════════════════════════════════════════════════════════════════════
    // BODY TRACKING TAB
    // Webcam / phone camera -> local MediaPipe World3D -> OSC trackers into
    // the OCU DLL's network tracker receiver (127.0.0.1, networkTrackerPort).
    // Left: body silhouette with tracker points lighting up as they track
    // (KAT treadmill shows as a puck under the feet). Right: live video with
    // the detected skeleton drawn over it.
    //
    // MediaPipe's 33-point metric skeleton is the only active camera source.
    // The older ONNX/RTMW implementation remains below as dormant migration
    // code, but World3D never loads, runs, previews or publishes from it.
    // ═══════════════════════════════════════════════════════════════════════
    public partial class MainForm
    {
        private Button _btnTabBody = null!;
        private Panel _tabBody = null!;

        private CheckBox _chkNetTrackersEnabled = null!;
        private CheckBox _chkCameraLegCalibration = null!;
        private CheckBox _chkWalkInPlace = null!;
        private ComboBox _cmbWalkActivation = null!;
        private ComboBox _cmbBodyDevice = null!;
        private ComboBox _cmbBodyPoseSource = null!;
        private volatile int _bodyDeviceSel = -1; // Dormant Legacy2D solver setting; World3D is MediaPipe CPU.
        // World3D is the only supported camera tracking source. Properties keep
        // the old branches compilable for now while making the runtime choice
        // immutable and preventing an old ui.json from re-enabling Legacy2D.
        private bool _bodyRequestedWorld3D => true;
        private bool _bodyUseWorld3D => true;
        private int _bodyActiveTrackerSource;
        private bool _bodyUiInitialized;
        private Label _lblBodyPoseSourceStatus = null!;
        private NumericUpDown _nudBodyOffX = null!;
        private NumericUpDown _nudBodyOffY = null!;
        private volatile float _bodyOffX; // percent of frame, applied to every keypoint
        private volatile float _bodyOffY; // positive = down
        private ComboBox _cmbBodyCamera = null!;
        private TextBox _txtBodyCamUrl = null!;
        private Button _btnBodyStartStop = null!;
        private CheckBox _chkBodyMirror = null!;
        private CheckBox _chkBodyStream = null!;
        private CheckBox _chkBodyPreview = null!;
        private CheckBox _chkBodySkeletonOnly = null!;
        private NumericUpDown _nudBodyHeightCm = null!;
        private Label _lblBodyStatus = null!;
        private PictureBox _picBodyVideo = null!;
        private BodySilhouettePanel _pnlBodySilhouette = null!;
        private ComboBox _cmbBodyCaptureView = null!;
        private ComboBox _cmbBodyCaptureAction = null!;
        private ComboBox _cmbBodyCaptureLeg = null!;
        private NumericUpDown _nudBodyCaptureTake = null!;
        private Button _btnBodyCaptureRecord = null!;
        private Label _lblBodyCaptureStatus = null!;

        // Video is only a setup aid. Tracking and OSC continue when the preview
        // is disabled, on another tab, or the Configurator is minimized.
        private volatile bool _bodyPreviewActive = true;
        private volatile bool _bodyPreviewMirror = true;
        private volatile bool _bodyPreviewSkeletonOnly;
        private volatile bool _bodyStreamEnabled = true;
        private int _bodyPreviewUiPending;
        private int _bodyPreviewModeVersion;
        private long _bodyPreviewLastFrameMs;
        private const int BodyPreviewIntervalMs = 100; // 10 FPS is plenty for camera setup
        private const int BodyPreviewMaxEdge = 960;
        private const int BodyInferenceIntervalMs = 33; // cameras are 30 FPS; don't burn cycles beyond their data rate

        private Thread? _bodyCapThread;
        private volatile bool _bodyCapRunning;
        private int _bodyCaptureGeneration;
        private volatile bool _bodyTrackingStreamReady;
        private volatile bool _bodyCalibrationRecording;
        private volatile string _bodyCalibrationView = "0";
        private volatile string _bodyCalibrationAction = "neutral";
        private volatile string _bodyCalibrationLeg = "both";
        private volatile string _bodyCalibrationFileName = "";
        private int _bodyCalibrationTake = 1;
        private int _bodyCalibrationSegment;
        private UdpClient? _bodyOsc;
        private int _bodyOscPort = 9000;
        private readonly object _bodyOscGate = new object();
        private readonly Dictionary<string, byte[]> _bodyOscPackets = new(StringComparer.Ordinal);

        private const int CocoBodyKeypointCount = 17;
        private const int BodyFootKeypointCount = 23;
        private const int TrackedKeypointCount = 133;
        private const float BodyJointMinConfidence = 0.30f;
        // RTMW's ankle/foot scores fall sharply when a leg points toward the
        // camera even though the reported landmark still follows the limb.
        // Treat those lower-body points as weak measurements instead of
        // freezing them at the exact moment a knee extends into a kick.
        private const float LegJointMinConfidence = 0.10f;
        // Camera skeleton coordinates are deliberately expressed against one
        // fixed reference body size. The game performs the real player-height
        // scale from the HMD, so a stale hidden HeightCm setting must never
        // alter kick reach, lift thresholds, or the calibration dataset.
        private const float BodyReferenceHeightM = 1.75f;
        private const int LeftHandStart = 91;
        private const int RightHandStart = 112;
        private const int HandKeypointCount = 21;

        // Latest keypoints (normalized 0..1 image coords + confidence),
        // Full COCO-WholeBody order (body, feet, face, both hands). Written by
        // capture, read by the UI; face remains hidden under the VR headset.
        private readonly float[,] _bodyKp = new float[TrackedKeypointCount, 3];
        private readonly object _bodyKpLock = new object();
        private volatile bool _bodyPoseValid;
        private volatile bool _bodyKatDetected;

        // Stable camera-to-body calibration. A per-frame ankle average cannot
        // be used as the floor: lifting one foot moves that average and erases
        // half the lift (and a bad frame can explode the body scale).
        private bool _bodyFloorValid;
        private float _bodyFloorY;
        private bool _bodyScaleValid;
        private float _bodyMetersPerUnit;

        // The camera pose is monocular, but apparent thigh/shin shortening
        // still carries useful out-of-plane motion. Remember each leg's
        // longest planted projection and recover a conservative forward depth
        // with Pythagoras so a real knee lift/kick is not flattened to z=0.
        private readonly float[] _bodyThighProjection = new float[2];
        private readonly float[] _bodyShinProjection = new float[2];
        private readonly float[] _bodyKneeDepth = new float[2];
        private readonly float[] _bodyFootDepth = new float[2];
        private readonly long[] _bodyDepthUpdateTimeMs = new long[2];
        private readonly float[] _bodyLegStraightness = new float[2];
        private readonly float[] _bodyFootLift = new float[2];
        // Planted, hip-relative lateral foot offsets in meters. Forward kicks
        // should extend along body-forward, not inherit monocular ankle jitter
        // as a half-meter sideways target.
        private readonly float[] _bodyFootNeutralOffsetM = new float[2];
        private readonly bool[] _bodyFootNeutralValid = new bool[2];

        // Exact virtual foot positions emitted to the OSC receiver, plus their
        // source-frame finite-difference velocity. Capturing these alongside
        // landmarks lets us replay the values seen by gait/kick arbitration.
        private readonly float[,] _bodyOutputFootPosition = new float[2, 3];
        private readonly float[,] _bodyOutputFootVelocity = new float[2, 3];
        private readonly bool[] _bodyOutputFootValid = new bool[2];
        private readonly long[] _bodyOutputFootTimeMs = new long[2];
        private readonly float[] _bodyOutputArmFeature = new float[2];
        private readonly float[] _bodyOutputHandY = new float[2];
        private float _bodyOutputArmPhase;
        private bool _bodyOutputArmValid;

        // Smoothed virtual-tracker euler rotations: waist, left foot, right
        // foot. Physical Vive trackers deliver orientation as well as position;
        // keeping this state turns the camera feed into the same 6DoF contract.
        private readonly float[,] _bodyTrackerEuler = new float[3, 3];
        private readonly bool[] _bodyTrackerEulerValid = new bool[3];

        // Camera-skeleton arm phase. The gait detector must not depend on
        // inside-out controller position because Quest controllers glide when
        // the headset cameras lose them below/behind the torso. A slow center
        // removes per-person asymmetry from the left-vs-right arm feature.
        private bool _bodyArmPhaseValid;
        private float _bodyArmPhaseCenter;

        // A pose is not a kick merely because one camera frame reports a high
        // or straight foot. This state machine learns a neutral baseline for
        // each leg and requires the timed chamber -> extension sequence seen in
        // the calibration captures. It also rejects the ankle-over-knee flips
        // RTMW emits when a front-facing kick briefly self-occludes.
        private readonly CameraLowerBodyClassifier _bodyLegClassifier = new();
        private CameraLowerBodyClassification _bodyLegClassification;

        private const string FbtModUrl = "https://www.nexusmods.com/skyrimspecialedition/mods/185070";

        private static readonly (string label, string id)[] BodyCaptureViews =
        {
            ("Front (0 deg)", "0"),
            ("Turn left (-45 deg)", "-45"),
            ("Turn right (+45 deg)", "+45"),
            ("Left profile (-90 deg)", "-90"),
            ("Right profile (+90 deg)", "+90"),
        };

        private static readonly (string label, string id)[] BodyCaptureActions =
        {
            ("Neutral stance", "neutral"),
            ("Slow walk", "walk_slow"),
            ("Normal walk", "walk_normal"),
            ("Run in place", "run"),
            ("Knee raise", "knee_raise"),
            ("Front kick", "front_kick"),
            ("High march (not a kick)", "high_march"),
        };

        private static readonly (string label, string id)[] BodyCaptureLegs =
        {
            ("Both / N/A", "both"),
            ("Left", "left"),
            ("Right", "right"),
        };

        // Indices 5..22, in COCO-WholeBody order. Face landmarks are omitted;
        // shoulders through toes are the geometry needed by the lower-body
        // classifier and keep the capture files reasonably small.
        private static readonly string[] BodyCaptureJointNames =
        {
            "lshoulder", "rshoulder", "lelbow", "relbow", "lwrist", "rwrist",
            "lhip", "rhip", "lknee", "rknee", "lankle", "rankle",
            "lbigtoe", "lsmalltoe", "lheel", "rbigtoe", "rsmalltoe", "rheel",
        };

        // COCO-WholeBody indices. 0..16 are COCO body, 17..22 are the six
        // RTMW foot landmarks that the previous decoder silently discarded.
        private const int KpNose = 0, KpLSho = 5, KpRSho = 6, KpLElb = 7, KpRElb = 8,
            KpLWri = 9, KpRWri = 10, KpLHip = 11, KpRHip = 12, KpLKnee = 13, KpRKnee = 14,
            KpLAnk = 15, KpRAnk = 16,
            KpLBigToe = 17, KpLSmallToe = 18, KpLHeel = 19,
            KpRBigToe = 20, KpRSmallToe = 21, KpRHeel = 22;

        private static readonly (int a, int b)[] SkeletonBones =
        {
            (KpLSho, KpRSho), (KpLSho, KpLElb), (KpLElb, KpLWri),
            (KpRSho, KpRElb), (KpRElb, KpRWri),
            (KpLSho, KpLHip), (KpRSho, KpRHip), (KpLHip, KpRHip),
            (KpLHip, KpLKnee), (KpLKnee, KpLAnk),
            (KpRHip, KpRKnee), (KpRKnee, KpRAnk),
            (KpLAnk, KpLHeel), (KpLHeel, KpLBigToe), (KpLBigToe, KpLSmallToe), (KpLSmallToe, KpLHeel),
            (KpRAnk, KpRHeel), (KpRHeel, KpRBigToe), (KpRBigToe, KpRSmallToe), (KpRSmallToe, KpRHeel),
        };

        // MediaPipe Pose's normalized 33-point topology. This is used only for
        // the preview when the continuous World 3D frame actually owns output;
        // emitted positions still come from metric WorldPosition landmarks.
        private static readonly (MediaPipe33LandmarkIndex a, MediaPipe33LandmarkIndex b)[] MediaPipePoseBones =
        {
            (MediaPipe33LandmarkIndex.LeftShoulder, MediaPipe33LandmarkIndex.RightShoulder),
            (MediaPipe33LandmarkIndex.LeftShoulder, MediaPipe33LandmarkIndex.LeftElbow),
            (MediaPipe33LandmarkIndex.LeftElbow, MediaPipe33LandmarkIndex.LeftWrist),
            (MediaPipe33LandmarkIndex.LeftWrist, MediaPipe33LandmarkIndex.LeftPinky),
            (MediaPipe33LandmarkIndex.LeftWrist, MediaPipe33LandmarkIndex.LeftIndex),
            (MediaPipe33LandmarkIndex.LeftWrist, MediaPipe33LandmarkIndex.LeftThumb),
            (MediaPipe33LandmarkIndex.RightShoulder, MediaPipe33LandmarkIndex.RightElbow),
            (MediaPipe33LandmarkIndex.RightElbow, MediaPipe33LandmarkIndex.RightWrist),
            (MediaPipe33LandmarkIndex.RightWrist, MediaPipe33LandmarkIndex.RightPinky),
            (MediaPipe33LandmarkIndex.RightWrist, MediaPipe33LandmarkIndex.RightIndex),
            (MediaPipe33LandmarkIndex.RightWrist, MediaPipe33LandmarkIndex.RightThumb),
            (MediaPipe33LandmarkIndex.LeftShoulder, MediaPipe33LandmarkIndex.LeftHip),
            (MediaPipe33LandmarkIndex.RightShoulder, MediaPipe33LandmarkIndex.RightHip),
            (MediaPipe33LandmarkIndex.LeftHip, MediaPipe33LandmarkIndex.RightHip),
            (MediaPipe33LandmarkIndex.LeftHip, MediaPipe33LandmarkIndex.LeftKnee),
            (MediaPipe33LandmarkIndex.LeftKnee, MediaPipe33LandmarkIndex.LeftAnkle),
            (MediaPipe33LandmarkIndex.LeftAnkle, MediaPipe33LandmarkIndex.LeftHeel),
            (MediaPipe33LandmarkIndex.LeftHeel, MediaPipe33LandmarkIndex.LeftFootIndex),
            (MediaPipe33LandmarkIndex.LeftAnkle, MediaPipe33LandmarkIndex.LeftFootIndex),
            (MediaPipe33LandmarkIndex.RightHip, MediaPipe33LandmarkIndex.RightKnee),
            (MediaPipe33LandmarkIndex.RightKnee, MediaPipe33LandmarkIndex.RightAnkle),
            (MediaPipe33LandmarkIndex.RightAnkle, MediaPipe33LandmarkIndex.RightHeel),
            (MediaPipe33LandmarkIndex.RightHeel, MediaPipe33LandmarkIndex.RightFootIndex),
            (MediaPipe33LandmarkIndex.RightAnkle, MediaPipe33LandmarkIndex.RightFootIndex),
        };

        // Relative 21-point hand topology: wrist, four thumb joints, then four
        // joints for index/middle/ring/pinky.
        private static readonly (int a, int b)[] HandBones =
        {
            (0, 1), (1, 2), (2, 3), (3, 4),
            (0, 5), (5, 6), (6, 7), (7, 8),
            (0, 9), (9, 10), (10, 11), (11, 12),
            (0, 13), (13, 14), (14, 15), (15, 16),
            (0, 17), (17, 18), (18, 19), (19, 20),
        };

        // Wrist plus the four finger roots form a steadier arm endpoint than
        // the single COCO wrist point when the full hand head is visible.
        private static readonly int[] PalmPointOffsets = { 0, 5, 9, 13, 17 };

        private void BuildBodyTrackingTab()
        {
            var container = _tabBody;
            int leftMargin = 6;
            int rightEdge = container.ClientSize.Width - 20;
            int y = 10;

            var btnBodySave = MakeButton("Save opencomposite.ini", rightEdge - 200, y - 4, 200, 30);
            btnBodySave.BackColor = Color.FromArgb(40, 120, 40);
            btnBodySave.ForeColor = Color.White;
            btnBodySave.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            btnBodySave.Click += BtnSave_Click;
            container.Controls.Add(btnBodySave);

            // ── Setup instructions (compact — details live in the panels below) ──
            var lblSteps = new Label
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin - 210, 66),
                Text = "1. Camera 2-4 m away, full body in frame (webcam below, or a phone IP-camera app's URL).\n"
                     + "2. Face the camera and press Start - local MediaPipe Lite tracks you; no camera data leaves this PC.\n"
                     + "3. Tick \"Send trackers to the game\" + Save. In-game body size auto-calibrates to your headset.\n"
                     + "4. In the game, hold both grips 2s to calibrate the FBT mod (download link below).",
                ForeColor = Color.FromArgb(190, 190, 195),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false
            };
            container.Controls.Add(lblSteps);
            y += 70;

            // ── Source row ──
            container.Controls.Add(MakeLabel("Camera:", leftMargin, y + 3, 60));
            _cmbBodyCamera = new ComboBox
            {
                Location = new Point(leftMargin + 60, y), Width = 170,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            _cmbBodyCamera.Items.AddRange(new object[]
                { "Webcam", "Phone / IP camera (URL)" });
            _cmbBodyCamera.SelectedIndex = 0;
            _cmbBodyCamera.SelectedIndexChanged += (s, e) =>
                _txtBodyCamUrl.Enabled = _cmbBodyCamera.SelectedIndex == 1;
            container.Controls.Add(_cmbBodyCamera);

            _txtBodyCamUrl = new TextBox
            {
                Location = new Point(leftMargin + 238, y), Width = 260,
                Text = "http://192.168.1.100:8080/video",
                Enabled = false,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            container.Controls.Add(_txtBodyCamUrl);

            _btnBodyStartStop = new ModernPillButton
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
            _chkBodyMirror.CheckedChanged += (s, e) => _bodyPreviewMirror = _chkBodyMirror.Checked;
            container.Controls.Add(_chkBodyMirror);

            _chkBodyStream = MakeCheckBox("Stream to OCU (OSC)", leftMargin + 80, y);
            _chkBodyStream.Checked = true;
            _chkBodyStream.CheckedChanged += (s, e) => _bodyStreamEnabled = _chkBodyStream.Checked;
            container.Controls.Add(_chkBodyStream);

            _chkBodyPreview = MakeCheckBox("Show preview", leftMargin + 245, y);
            _chkBodyPreview.Checked = true;
            _chkBodyPreview.CheckedChanged += (s, e) =>
            {
                _chkBodySkeletonOnly.Enabled = _chkBodyPreview.Checked;
                Interlocked.Increment(ref _bodyPreviewModeVersion);
                UpdateBodyPreviewActivity();
            };
            container.Controls.Add(_chkBodyPreview);
            new ToolTip { AutoPopDelay = 12000, InitialDelay = 350 }.SetToolTip(_chkBodyPreview,
                "Display only. Turn this off to skip video conversion, skeleton drawing, and UI repainting while tracking and OSC keep running. Preview also pauses on other pages and when minimized.");

            _chkBodySkeletonOnly = MakeCheckBox("Skeleton only", leftMargin + 365, y);
            _chkBodySkeletonOnly.Checked = true;
            _chkBodySkeletonOnly.CheckedChanged += (s, e) =>
            {
                _bodyPreviewSkeletonOnly = _chkBodySkeletonOnly.Checked;
                Interlocked.Increment(ref _bodyPreviewModeVersion);
                Interlocked.Exchange(ref _bodyPreviewLastFrameMs, 0);
                var old = _picBodyVideo?.Image;
                if (_picBodyVideo != null)
                    _picBodyVideo.Image = null;
                old?.Dispose();
            };
            container.Controls.Add(_chkBodySkeletonOnly);
            new ToolTip { AutoPopDelay = 12000, InitialDelay = 350 }.SetToolTip(_chkBodySkeletonOnly,
                "Hide the camera image and draw only the detected skeleton on black. Camera capture, pose AI, feet/finger tracking, and OSC continue normally. This skips video-to-bitmap conversion but not pose inference.");

            // Height input retired from the UI: the OCU DLL auto-scales the
            // skeleton against the real HMD height in-game, so a manual value
            // is redundant. The hidden control keeps the plumbing satisfied.
            _nudBodyHeightCm = new NumericUpDown
            {
                Minimum = 120, Maximum = 220, Value = 175, Visible = false
            };

            _lblBodyStatus = MakeLabel("Idle", leftMargin + 475, y + 2, rightEdge - leftMargin - 475);
            _lblBodyStatus.ForeColor = Color.FromArgb(160, 160, 160);
            container.Controls.Add(_lblBodyStatus);
            y += 26;

            _chkNetTrackersEnabled = MakeCheckBox("Send trackers to the game (networkTrackersEnabled, needs game restart)", leftMargin, y);
            container.Controls.Add(_chkNetTrackersEnabled);
            // MediaPipe's native World3D task is the sole pose engine and runs
            // locally on CPU. The disabled field makes that runtime contract
            // explicit instead of exposing the dormant RTMW device selector.
            container.Controls.Add(MakeLabel("AI device:", leftMargin + 480, y + 2, 70));
            _cmbBodyDevice = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(leftMargin + 552, y - 1), Width = 300,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White
            };
            _cmbBodyDevice.Items.Add("CPU (MediaPipe World3D Lite, local)");
            _cmbBodyDevice.SelectedIndex = 0;
            _cmbBodyDevice.Enabled = false;
            new ToolTip().SetToolTip(_cmbBodyDevice,
                "MediaPipe World3D Lite runs locally on CPU. RTMW/Legacy2D is not loaded.");
            container.Controls.Add(_cmbBodyDevice);
            y += 26;

            // One immutable source owns trackers, gait, recording and preview.
            // No secondary 2D solver is kept warm and no fallback can change
            // coordinate systems during a MediaPipe dropout.
            container.Controls.Add(MakeLabel("FBT tracker pose:", leftMargin, y + 2, 112));
            _cmbBodyPoseSource = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(leftMargin + 112, y - 1), Width = 330,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            _cmbBodyPoseSource.Items.Add("World 3D landmarks (MediaPipe, local)");
            _cmbBodyPoseSource.SelectedIndex = 0;
            _cmbBodyPoseSource.Enabled = false;
            new ToolTip { AutoPopDelay = 15000, InitialDelay = 350 }.SetToolTip(_cmbBodyPoseSource,
                "World 3D uses MediaPipe's continuous hip-centered 3D skeleton for waist, knees and feet.\n" +
                "It bypasses the old gait-state depth guesses, so a straight kick stays a straight leg.\n" +
                "MediaPipe shoulders, elbows and wrists also drive walking rhythm.\n" +
                "Everything is processed locally; no RTMW fallback or Google metrics uploader runs.");
            container.Controls.Add(_cmbBodyPoseSource);
            _lblBodyPoseSourceStatus = MakeLabel(
                "Mode: World 3D (MediaPipe) | Active: stopped",
                leftMargin + 452,
                y + 2,
                Math.Max(210, rightEdge - leftMargin - 452));
            _lblBodyPoseSourceStatus.ForeColor = Color.FromArgb(145, 165, 175);
            _lblBodyPoseSourceStatus.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            container.Controls.Add(_lblBodyPoseSourceStatus);
            _cmbBodyPoseSource.SelectedIndexChanged += (s, e) => OnBodyPoseSourceChanged();
            y += 26;

            // Display-only alignment for the camera overlay. It never changes
            // metric tracker geometry, classifier input or recorded landmarks.
            container.Controls.Add(MakeLabel(
                "Preview skeleton nudge:  right +",
                leftMargin, y + 2, 185));
            _nudBodyOffX = new NumericUpDown
            {
                Location = new Point(leftMargin + 185, y), Width = 60,
                DecimalPlaces = 1, Increment = 0.5m, Minimum = -20, Maximum = 20, Value = 0,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            _nudBodyOffX.ValueChanged += (s, e) => _bodyOffX = (float)_nudBodyOffX.Value;
            container.Controls.Add(_nudBodyOffX);
            container.Controls.Add(MakeLabel("down +", leftMargin + 255, y + 2, 55));
            _nudBodyOffY = new NumericUpDown
            {
                Location = new Point(leftMargin + 310, y), Width = 60,
                DecimalPlaces = 1, Increment = 0.5m, Minimum = -20, Maximum = 20, Value = 0,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            _nudBodyOffY.ValueChanged += (s, e) => _bodyOffY = (float)_nudBodyOffY.Value;
            container.Controls.Add(_nudBodyOffY);
            container.Controls.Add(MakeLabel("(% of frame, preview only)", leftMargin + 378, y + 2, 160));
            y += 26;

            // ── Left: silhouette / Right: video ──
            int panelH = 330;
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

            // Labeled motion capture for tuning the camera FBT classifier.
            // This is diagnostics only: it records landmarks and derived leg
            // geometry, never camera frames, and does not alter tracker output.
            var pnlCapture = new Panel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 94),
                BackColor = Color.FromArgb(48, 42, 60),
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lblCaptureTitle = MakeLabel("FBT motion calibration capture", 8, 4, pnlCapture.Width - 16);
            lblCaptureTitle.ForeColor = Color.FromArgb(205, 175, 245);
            lblCaptureTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            pnlCapture.Controls.Add(lblCaptureTitle);

            pnlCapture.Controls.Add(MakeLabel("View:", 8, 31, 38));
            _cmbBodyCaptureView = new ComboBox
            {
                Location = new Point(46, 27), Width = 155,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            foreach (var option in BodyCaptureViews) _cmbBodyCaptureView.Items.Add(option.label);
            _cmbBodyCaptureView.SelectedIndex = 0;
            pnlCapture.Controls.Add(_cmbBodyCaptureView);

            pnlCapture.Controls.Add(MakeLabel("Action:", 211, 31, 45));
            _cmbBodyCaptureAction = new ComboBox
            {
                Location = new Point(256, 27), Width = 170,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            foreach (var option in BodyCaptureActions) _cmbBodyCaptureAction.Items.Add(option.label);
            _cmbBodyCaptureAction.SelectedIndex = 0;
            pnlCapture.Controls.Add(_cmbBodyCaptureAction);

            pnlCapture.Controls.Add(MakeLabel("Leg:", 436, 31, 32));
            _cmbBodyCaptureLeg = new ComboBox
            {
                Location = new Point(468, 27), Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            foreach (var option in BodyCaptureLegs) _cmbBodyCaptureLeg.Items.Add(option.label);
            _cmbBodyCaptureLeg.SelectedIndex = 0;
            pnlCapture.Controls.Add(_cmbBodyCaptureLeg);

            pnlCapture.Controls.Add(MakeLabel("Take:", 578, 31, 35));
            _nudBodyCaptureTake = new NumericUpDown
            {
                Location = new Point(613, 27), Width = 48,
                Minimum = 1, Maximum = 99, Value = 1,
                BackColor = Color.FromArgb(50, 50, 55), ForeColor = Color.White,
            };
            pnlCapture.Controls.Add(_nudBodyCaptureTake);

            _btnBodyCaptureRecord = new ModernPillButton
            {
                Location = new Point(674, 25), Size = new Size(132, 27),
                Text = "Record sample",
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.White, BackColor = Color.FromArgb(95, 55, 135),
                Cursor = Cursors.Hand,
            };
            _btnBodyCaptureRecord.Click += (s, e) => ToggleBodyCalibrationRecord();
            pnlCapture.Controls.Add(_btnBodyCaptureRecord);

            _lblBodyCaptureStatus = MakeLabel(
                "Start tracking, choose labels, Record, perform one sample, then Stop recording.",
                8, 60, pnlCapture.Width - 16);
            _lblBodyCaptureStatus.ForeColor = Color.FromArgb(175, 165, 190);
            _lblBodyCaptureStatus.Font = new Font("Segoe UI", 8f);
            pnlCapture.Controls.Add(_lblBodyCaptureStatus);
            // Dev-only: the controls stay constructed (other code references
            // them) but the panel is only shown with devtools.on present.
            if (ShowDevTools)
            {
                container.Controls.Add(pnlCapture);
                y += 102;
            }

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

            // Live controller trim for the public camera foot targets. Physical
            // Vive/Tundra trackers bypass this feature in the native runtime.
            var pnlLegCal = new Panel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 118),
                BackColor = Color.FromArgb(50, 43, 58),
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lblLegCalTitle = MakeLabel("Live Camera Foot Placement + Lift Range (in game)", 8, 4, pnlLegCal.Width - 16);
            lblLegCalTitle.ForeColor = Color.FromArgb(205, 175, 245);
            lblLegCalTitle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            pnlLegCal.Controls.Add(lblLegCalTitle);

            var lblLegCalBody = new Label
            {
                Location = new Point(8, 24),
                Size = new Size(pnlLegCal.Width - 16, 52),
                Text = "TRIPLE-TAP RIGHT A to enter/exit; OCU blocks game input while editing and auto-saves. Stick-clicks are never used.\n"
                     + "LEFT STICK controls the LEFT foot; RIGHT STICK controls the RIGHT foot. Normal stick moves left/right and forward/back.\n"
                     + "Hold that side's GRIP + stick to ROTATE its foot (X turns, Y pivots). LEFT X or either TRIGGER + stick Y adjusts LIFT RANGE. Modifier + triple RIGHT A resets both legs.",
                ForeColor = Color.FromArgb(195, 185, 205),
                Font = new Font("Segoe UI", 8.25f),
                AutoSize = false,
            };
            pnlLegCal.Controls.Add(lblLegCalBody);

            _chkCameraLegCalibration = MakeCheckBox(
                "Arm camera-leg calibration controls (Save + game restart required)", 8, 84);
            _chkCameraLegCalibration.Checked = ShowDevTools;
            _chkCameraLegCalibration.ForeColor = Color.FromArgb(190, 160, 235);
            pnlLegCal.Controls.Add(_chkCameraLegCalibration);

            var btnResetLegCal = MakeButton("Reset saved trim", pnlLegCal.Width - 152, 80, 142, 28);
            btnResetLegCal.BackColor = Color.FromArgb(90, 55, 105);
            btnResetLegCal.ForeColor = Color.White;
            btnResetLegCal.Click += (s, e) => ResetSavedCameraLegCalibration();
            pnlLegCal.Controls.Add(btnResetLegCal);
            if (ShowDevTools)
            {
                container.Controls.Add(pnlLegCal);
                y += 126;
            }

            // ── Full-body walking (walk-in-place locomotion) ──
            var pnlWalk = new Panel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 142),
                BackColor = Color.FromArgb(40, 52, 60),
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lblWalkTitle = new Label
            {
                Location = new Point(8, 4),
                Size = new Size(pnlWalk.Width - 16, 18),
                Text = "Full-Body Walking (walk in place to move)",
                ForeColor = Color.FromArgb(140, 200, 240),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            pnlWalk.Controls.Add(lblWalkTitle);
            var lblWalkBody = new Label
            {
                Location = new Point(8, 23),
                Size = new Size(pnlWalk.Width - 16, 52),
                Text = "FORWARD - Skeleton ankles/knees + opposite shoulder-elbow-wrist swings drive gait; a selected hold button lowers the first-step gate.\n"
                     + "BACKWARD - Keep both skeleton hands near chin height while marching; lower them to go forward again.\n"
                     + "TURN - Point the right controller's head right, or the left controller's head left (palm toward the camera). Keep the other hand normal.",
                ForeColor = Color.FromArgb(175, 190, 200),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false
            };
            pnlWalk.Controls.Add(lblWalkBody);
            _chkWalkInPlace = MakeCheckBox("Enable Full-Body Walking (Save + game restart required)", 8, 76);
            _chkWalkInPlace.ForeColor = Color.FromArgb(140, 210, 240);
            _chkWalkInPlace.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            pnlWalk.Controls.Add(_chkWalkInPlace);

            pnlWalk.Controls.Add(MakeLabel("Hold to activate:", 8, 106, 102));
            _cmbWalkActivation = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(112, 102),
                Width = 300,
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = Color.White,
            };
            RefreshWalkActivationOptions();
            new ToolTip().SetToolTip(_cmbWalkActivation,
                "The physical buttons follow the Meta Touch or Index model selected on the Bindings page.\n" +
                "Hold this exact button to enable walking and get a quicker first step.\n" +
                "None requires no button and uses the normal rhythm gate.");
            pnlWalk.Controls.Add(_cmbWalkActivation);
            var lblWalkActivation = MakeLabel("Each hand/button is separate; controller mappings still resolve through the active OpenXR profile.", 424, 105, pnlWalk.Width - 432);
            lblWalkActivation.ForeColor = Color.FromArgb(145, 165, 175);
            lblWalkActivation.Font = new Font("Segoe UI", 8f, FontStyle.Italic);
            pnlWalk.Controls.Add(lblWalkActivation);
            container.Controls.Add(pnlWalk);
            y += 150;

            // ── Lighting guidance (field-tested: front light = night and day) ──
            var pnlLight = new Panel
            {
                Location = new Point(leftMargin, y),
                Size = new Size(rightEdge - leftMargin, 64),
                BackColor = Color.FromArgb(55, 50, 38),
                BorderStyle = BorderStyle.FixedSingle,
            };
            var lblLightTitle = new Label
            {
                Location = new Point(8, 4),
                Size = new Size(pnlLight.Width - 16, 18),
                Text = "Lighting makes or breaks tracking quality",
                ForeColor = Color.FromArgb(235, 200, 120),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            };
            pnlLight.Controls.Add(lblLightTitle);
            var lblLightBody = new Label
            {
                Location = new Point(8, 23),
                Size = new Size(pnlLight.Width - 16, 36),
                Text = "Light yourself EVENLY and DIRECTLY from the front - a lamp near the camera, pointed at you. Side lighting and "
                     + "backlighting (a window or lamp behind you) confuse the AI and cause jitter; remove them from the equation. "
                     + "Don't shine light into the camera lens itself.",
                ForeColor = Color.FromArgb(200, 190, 165),
                Font = new Font("Segoe UI", 8.5f),
                AutoSize = false
            };
            pnlLight.Controls.Add(lblLightBody);
            container.Controls.Add(pnlLight);
            y += 72;

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
                UpdateBodyPoseSourceStatus();
            };
            uiTimer.Start();

            _tabBody.VisibleChanged += (s, e) => UpdateBodyPreviewActivity();
            Resize += (s, e) => UpdateBodyPreviewActivity();
            FormClosing += (s, e) => SaveBodyUi();
            FormClosed += (s, e) => StopBodyCapture();

            LoadBodyUi();
            _bodyUiInitialized = true;
            UpdateBodyPoseSourceStatus();
            UpdateBodyPreviewActivity();
        }

        private void ResetSavedCameraLegCalibration()
        {
            try
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(_gameDir) && Directory.Exists(_gameDir))
                    paths.Add(Path.Combine(_gameDir, "camera_leg_calibration.ini"));

                // Under MO2 the runtime writes this game-root file through
                // USVFS, so its physical home is <instance>\overwrite\Root.
                // Reset both locations; deleting only the real game path leaves
                // the active overwrite value untouched and makes Reset appear
                // to work when it did nothing.
                string modDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\', '/');
                string modsDir = Path.GetDirectoryName(modDir) ?? "";
                if (string.Equals(Path.GetFileName(modsDir), "mods", StringComparison.OrdinalIgnoreCase))
                {
                    string instanceDir = Path.GetDirectoryName(modsDir) ?? "";
                    if (instanceDir.Length > 0)
                        paths.Add(Path.Combine(instanceDir, "overwrite", "Root", "camera_leg_calibration.ini"));
                }

                if (paths.Count == 0)
                    throw new DirectoryNotFoundException("Select the Skyrim VR game folder first.");
                foreach (string path in paths)
                    if (File.Exists(path))
                        File.Delete(path);
                _lblBodyStatus.Text = "Saved camera leg trim reset (restart game).";
                _lblBodyStatus.ForeColor = Color.FromArgb(135, 220, 150);
            }
            catch (Exception ex)
            {
                _lblBodyStatus.Text = $"Leg trim reset failed: {ex.Message}";
                _lblBodyStatus.ForeColor = Color.FromArgb(255, 115, 115);
            }
        }

        private string WalkActivationKey()
        {
            if (_cmbWalkActivation == null || _cmbWalkActivation.SelectedIndex < 0)
                return "none";
            var opts = CurrentHoldOptions;
            int i = _cmbWalkActivation.SelectedIndex;
            return i < opts.Length && opts[i].id.Length > 0 ? opts[i].id : "none";
        }

        // Keep walking on the exact same per-controller/per-hand button model
        // used by the Gestures and Bindings pages instead of maintaining a
        // second generic button vocabulary.
        private void RefreshWalkActivationOptions()
        {
            if (_cmbWalkActivation == null) return;
            string keep = WalkActivationKey();
            _cmbWalkActivation.Items.Clear();
            foreach (var opt in CurrentHoldOptions)
                _cmbWalkActivation.Items.Add(opt.id.Length == 0 ? "None (walking is always active)" : opt.label);
            int idx = Array.FindIndex(CurrentHoldOptions, o => o.id == keep);
            _cmbWalkActivation.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void SelectWalkActivation(string key)
        {
            string selected = key.Trim().ToLowerInvariant() switch
            {
                // Migrate the old hand-agnostic choices without breaking an
                // existing config. New saves always carry an exact hand.
                "grip" => "r_grip",
                "trigger" => "r_trigger",
                "primary" => "r_a",
                "secondary" => "r_b",
                "thumbstick" => "r_stick",
                "trackpad" => "r_trackpad",
                "none" => "",
                _ => key.Trim().ToLowerInvariant(),
            };
            int idx = Array.FindIndex(CurrentHoldOptions, o => o.id == selected);
            _cmbWalkActivation.SelectedIndex = idx >= 0 ? idx : 0;
        }

        // ── Tab-state persistence (BodyTracking\ui.json beside the exe) ──
        // Camera choice/URL and tuning survive relaunches; saved on Start and
        // on close so a fresh session comes up ready to go.

        private sealed class BodyUiState
        {
            public int CameraChoicesVersion { get; set; } = 0;
            public int CamIndex { get; set; } = 0;
            public string CamUrl { get; set; } = "";
            public int HeightCm { get; set; } = 175;
            public bool Mirror { get; set; } = true;
            public bool Stream { get; set; } = true;
            public bool Preview { get; set; } = true;
            public bool SkeletonOnly { get; set; } = false;
            public bool Gpu { get; set; } = true; // legacy; superseded by Device
            public string Device { get; set; } = ""; // "auto" | "cpu" | "gpu<N>"
            public string PoseSource { get; set; } = "world3d"; // Legacy values are ignored on load.
            public float OffX { get; set; }
            public float OffY { get; set; }
        }

        private string BodyUiPath => Path.Combine(AppContext.BaseDirectory, "BodyTracking", "ui.json");

        private void OnBodyPoseSourceChanged()
        {
            if (_cmbBodyPoseSource == null || _cmbBodyPoseSource.SelectedIndex < 0)
                return;

            if (_bodyUiInitialized)
                SaveBodyUi();
            UpdateBodyPoseSourceStatus();
        }

        private void UpdateBodyPoseSourceStatus()
        {
            if (_lblBodyPoseSourceStatus == null || _cmbBodyPoseSource == null)
                return;

            if (!_bodyCapRunning)
            {
                _lblBodyPoseSourceStatus.Text = "Mode: World 3D (MediaPipe) | Active: stopped";
                _lblBodyPoseSourceStatus.ForeColor = Color.FromArgb(145, 165, 175);
                return;
            }

            BodyTrackerSource active = (BodyTrackerSource)Volatile.Read(ref _bodyActiveTrackerSource);
            string activeText = active switch
            {
                BodyTrackerSource.Continuous3D => "tracking",
                _ => "waiting for pose",
            };
            _lblBodyPoseSourceStatus.Text = $"Mode: World 3D (MediaPipe) | Active: {activeText}";
            _lblBodyPoseSourceStatus.ForeColor = active switch
            {
                BodyTrackerSource.Continuous3D => Color.FromArgb(105, 215, 255),
                _ => Color.FromArgb(175, 165, 190),
            };
        }

        private void SaveBodyUi()
        {
            try
            {
                var st = new BodyUiState
                {
                    CameraChoicesVersion = 2,
                    CamIndex = _cmbBodyCamera.SelectedIndex,
                    CamUrl = _txtBodyCamUrl.Text,
                    HeightCm = (int)(BodyReferenceHeightM * 100f),
                    Mirror = _chkBodyMirror.Checked,
                    Stream = _chkBodyStream.Checked,
                    Preview = _chkBodyPreview.Checked,
                    SkeletonOnly = _chkBodySkeletonOnly.Checked,
                    Gpu = false,
                    Device = "cpu",
                    PoseSource = "world3d",
                    OffX = (float)_nudBodyOffX.Value,
                    OffY = (float)_nudBodyOffY.Value,
                };
                Directory.CreateDirectory(Path.GetDirectoryName(BodyUiPath)!);
                File.WriteAllText(BodyUiPath, System.Text.Json.JsonSerializer.Serialize(st));
            }
            catch { }
        }

        private void LoadBodyUi()
        {
            try
            {
                if (!File.Exists(BodyUiPath)) return;
                var st = System.Text.Json.JsonSerializer.Deserialize<BodyUiState>(File.ReadAllText(BodyUiPath));
                if (st == null) return;
                // Old builds stored 0..3 for four separate webcams and 4 for
                // IP camera. Collapse all legacy webcam indices to Webcam;
                // version 2 stores the new two-choice 0/1 layout directly.
                _cmbBodyCamera.SelectedIndex = st.CameraChoicesVersion >= 2
                    ? (st.CamIndex == 1 ? 1 : 0)
                    : (st.CamIndex == 4 ? 1 : 0);
                if (!string.IsNullOrWhiteSpace(st.CamUrl))
                    _txtBodyCamUrl.Text = st.CamUrl;
                // Height is now an in-game HMD calibration. Ignore legacy
                // hidden values here; older ui.json files could silently force
                // the camera solver as low as 120 cm.
                _nudBodyHeightCm.Value = (decimal)(BodyReferenceHeightM * 100f);
                _chkBodyMirror.Checked = st.Mirror;
                _chkBodyStream.Checked = st.Stream;
                _chkBodyPreview.Checked = st.Preview;
                _chkBodySkeletonOnly.Checked = st.SkeletonOnly;
                // World3D-only builds ignore the old RTMW device/source choice,
                // but preserve the user's display-only skeleton alignment.
                _cmbBodyDevice.SelectedIndex = 0;
                _cmbBodyPoseSource.SelectedIndex = 0;
                _nudBodyOffX.Value = (decimal)Math.Clamp(st.OffX, -20f, 20f);
                _nudBodyOffY.Value = (decimal)Math.Clamp(st.OffY, -20f, 20f);
                _bodyOffX = (float)_nudBodyOffX.Value;
                _bodyOffY = (float)_nudBodyOffY.Value;
            }
            catch { }
        }

        private void UpdateBodyPreviewActivity()
        {
            if (_chkBodyPreview == null || _tabBody == null || _picBodyVideo == null)
                return;

            bool active = _chkBodyPreview.Checked
                && _tabBody.Visible
                && WindowState != FormWindowState.Minimized;
            _bodyPreviewActive = active;

            if (!active)
            {
                Interlocked.Exchange(ref _bodyPreviewLastFrameMs, 0);
                var old = _picBodyVideo.Image;
                _picBodyVideo.Image = null;
                old?.Dispose();
            }
        }

        private void ToggleBodyCapture()
        {
            if (_bodyCapRunning) { StopBodyCapture(); return; }

            _bodyOscPort = 9000;
            if (int.TryParse(_ini.Get("input", "networkTrackerPort", _ini.Get("", "networkTrackerPort", "9000")), out int p))
                _bodyOscPort = p;

            int camIndex = _cmbBodyCamera.SelectedIndex;
            string source = camIndex == 1 ? _txtBodyCamUrl.Text.Trim() : "0";
            _bodyDeviceSel = -1; // Dormant Legacy2D path; MediaPipe owns this session.
            Volatile.Write(ref _bodyActiveTrackerSource, (int)BodyTrackerSource.None);
            SaveBodyUi(); // a successful Start config is worth remembering

            ResetBodyCoordinateCalibration();
            _bodyCalibrationFileName = "";
            _bodyCapRunning = true;
            int captureGeneration = Interlocked.Increment(ref _bodyCaptureGeneration);
            _btnBodyStartStop.Text = "Stop";
            _btnBodyStartStop.BackColor = Color.FromArgb(150, 45, 45);
            _lblBodyStatus.Text = "Opening camera...";
            _lblBodyStatus.ForeColor = Color.FromArgb(220, 200, 120);
            UpdateBodyPoseSourceStatus();

            _bodyCapThread = new Thread(() => BodyCaptureLoop(source, captureGeneration)) { IsBackground = true };
            _bodyCapThread.Start();
        }

        private void ToggleBodyCalibrationRecord()
        {
            if (_bodyCalibrationRecording)
            {
                _bodyCalibrationRecording = false;
                _btnBodyCaptureRecord.Text = "Record sample";
                _btnBodyCaptureRecord.BackColor = Color.FromArgb(95, 55, 135);
                _cmbBodyCaptureView.Enabled = true;
                _cmbBodyCaptureAction.Enabled = true;
                _cmbBodyCaptureLeg.Enabled = true;
                _nudBodyCaptureTake.Enabled = true;
                _lblBodyCaptureStatus.Text = string.IsNullOrEmpty(_bodyCalibrationFileName)
                    ? "Sample stopped."
                    : $"Sample saved in BodyTracking\\Captures\\{_bodyCalibrationFileName}";
                _lblBodyCaptureStatus.ForeColor = Color.FromArgb(170, 210, 170);
                return;
            }

            BodyTrackerSource activeSource =
                (BodyTrackerSource)Volatile.Read(ref _bodyActiveTrackerSource);
            if (!_bodyCapRunning
                || !_bodyTrackingStreamReady
                || activeSource != BodyTrackerSource.Continuous3D)
            {
                _lblBodyCaptureStatus.Text = "Wait for MediaPipe World3D: the full body and both feet must be locked before recording.";
                _lblBodyCaptureStatus.ForeColor = Color.FromArgb(245, 175, 105);
                return;
            }

            int view = Math.Clamp(_cmbBodyCaptureView.SelectedIndex, 0, BodyCaptureViews.Length - 1);
            int action = Math.Clamp(_cmbBodyCaptureAction.SelectedIndex, 0, BodyCaptureActions.Length - 1);
            int leg = Math.Clamp(_cmbBodyCaptureLeg.SelectedIndex, 0, BodyCaptureLegs.Length - 1);
            _bodyCalibrationView = BodyCaptureViews[view].id;
            _bodyCalibrationAction = BodyCaptureActions[action].id;
            _bodyCalibrationLeg = BodyCaptureLegs[leg].id;
            Volatile.Write(ref _bodyCalibrationTake, (int)_nudBodyCaptureTake.Value);
            Interlocked.Increment(ref _bodyCalibrationSegment);
            _bodyCalibrationRecording = true;

            _cmbBodyCaptureView.Enabled = false;
            _cmbBodyCaptureAction.Enabled = false;
            _cmbBodyCaptureLeg.Enabled = false;
            _nudBodyCaptureTake.Enabled = false;
            _btnBodyCaptureRecord.Text = "Stop recording";
            _btnBodyCaptureRecord.BackColor = Color.FromArgb(155, 45, 65);
            _lblBodyCaptureStatus.Text = $"RECORDING: {BodyCaptureViews[view].label} / {BodyCaptureActions[action].label} / {BodyCaptureLegs[leg].label}";
            _lblBodyCaptureStatus.ForeColor = Color.FromArgb(255, 135, 155);
        }

        private void StopBodyCalibrationRecord(string status)
        {
            _bodyCalibrationRecording = false;
            if (_btnBodyCaptureRecord == null || IsDisposed) return;
            _btnBodyCaptureRecord.Text = "Record sample";
            _btnBodyCaptureRecord.BackColor = Color.FromArgb(95, 55, 135);
            _cmbBodyCaptureView.Enabled = true;
            _cmbBodyCaptureAction.Enabled = true;
            _cmbBodyCaptureLeg.Enabled = true;
            _nudBodyCaptureTake.Enabled = true;
            _lblBodyCaptureStatus.Text = status;
            _lblBodyCaptureStatus.ForeColor = Color.FromArgb(175, 165, 190);
        }

        private void BodySetCaptureStatus(string text, Color color)
        {
            try
            {
                BeginInvoke(() =>
                {
                    if (_lblBodyCaptureStatus == null || IsDisposed) return;
                    _lblBodyCaptureStatus.Text = text;
                    _lblBodyCaptureStatus.ForeColor = color;
                });
            }
            catch { }
        }

        // Losing the lower body is not just a missing-frame event. The next
        // full-frame pose can use a different crop/origin, so every value that
        // converts image coordinates into virtual tracker space must be
        // relearned before packets resume.
        private void ResetBodyCoordinateCalibration()
        {
            _bodyFloorValid = false;
            _bodyScaleValid = false;
            Array.Clear(_bodyThighProjection);
            Array.Clear(_bodyShinProjection);
            Array.Clear(_bodyKneeDepth);
            Array.Clear(_bodyFootDepth);
            Array.Clear(_bodyDepthUpdateTimeMs);
            Array.Clear(_bodyLegStraightness);
            Array.Clear(_bodyFootLift);
            Array.Clear(_bodyFootNeutralOffsetM);
            Array.Clear(_bodyFootNeutralValid);
            Array.Clear(_bodyOutputFootPosition);
            Array.Clear(_bodyOutputFootVelocity);
            Array.Clear(_bodyOutputFootValid);
            Array.Clear(_bodyOutputFootTimeMs);
            Array.Clear(_bodyOutputArmFeature);
            Array.Clear(_bodyOutputHandY);
            _bodyOutputArmPhase = 0f;
            _bodyOutputArmValid = false;
            Array.Clear(_bodyTrackerEuler);
            Array.Clear(_bodyTrackerEulerValid);
            _bodyArmPhaseValid = false;
            _bodyArmPhaseCenter = 0f;
            _bodyLegClassifier.Reset();
            _bodyLegClassification = default;
        }

        private void StopBodyCapture()
        {
            _bodyCapRunning = false;
            Interlocked.Increment(ref _bodyCaptureGeneration);
            _bodyTrackingStreamReady = false;
            Volatile.Write(ref _bodyActiveTrackerSource, (int)BodyTrackerSource.None);
            StopBodyCalibrationRecord("Tracking stopped. Calibration file was closed safely.");
            Thread? stoppingThread = _bodyCapThread;
            try { stoppingThread?.Join(1500); } catch { }
            if (ReferenceEquals(_bodyCapThread, stoppingThread))
                _bodyCapThread = null;
            // A normally exiting capture thread clears and detaches its own
            // socket in finally. If it is still blocked after the bounded join,
            // claim that current socket here, clear it, and prevent the delayed
            // old session from clearing any replacement session later.
            UdpClient? stoppingOsc = ClearBodyOscSession(expectedSession: null);
            try { stoppingOsc?.Dispose(); } catch { }
            _bodyPoseValid = false;
            var oldPreview = _picBodyVideo?.Image;
            if (_picBodyVideo != null)
                _picBodyVideo.Image = null;
            oldPreview?.Dispose();
            if (_btnBodyStartStop != null && !IsDisposed)
            {
                _btnBodyStartStop.Text = "Start";
                _btnBodyStartStop.BackColor = Color.FromArgb(40, 120, 40);
                _lblBodyStatus.Text = "Idle";
                _lblBodyStatus.ForeColor = Color.FromArgb(160, 160, 160);
            }
            UpdateBodyPoseSourceStatus();
        }

        private bool IsBodyCaptureSessionActive(int captureGeneration) =>
            _bodyCapRunning
            && Volatile.Read(ref _bodyCaptureGeneration) == captureGeneration;

        private void BodySetStatus(string text, Color color)
        {
            try
            {
                BeginInvoke(() => { _lblBodyStatus.Text = text; _lblBodyStatus.ForeColor = color; });
            }
            catch { }
        }

        // ── Capture worker ────────────────────────────────────────────────

        private static void WriteBodyCalibrationHeader(StreamWriter writer)
        {
            var line = new System.Text.StringBuilder(
                "t_ms;utc_iso;segment;view_yaw_deg;action;leg;take;stream_ready;mean_core_conf;mean_foot_conf;reference_height_m;floor_valid;floor_y;scale_valid;meters_per_unit;lknee_angle_deg;rknee_angle_deg;lstraight;rstraight;lknee_depth;rknee_depth;lfoot_depth;rfoot_depth;llift;rlift;lout_valid;lout_x;lout_y;lout_z;lout_vx;lout_vy;lout_vz;rout_valid;rout_x;rout_y;rout_z;rout_vx;rout_vy;rout_vz;arms_valid;larm_feature;rarm_feature;arm_phase;lhand_y;rhand_y;lstate;rstate;ltransport_state;rtransport_state;lstate_conf;rstate_conf;lflip_rejected;rflip_rejected;coordinated_gait");
            foreach (string joint in BodyCaptureJointNames)
            {
                line.Append(";f_").Append(joint).Append("_x");
                line.Append(";f_").Append(joint).Append("_y");
                line.Append(";f_").Append(joint).Append("_conf");
                line.Append(";r_").Append(joint).Append("_x");
                line.Append(";r_").Append(joint).Append("_y");
                line.Append(";r_").Append(joint).Append("_conf");
            }
            writer.WriteLine(line.ToString());
        }

        private void WriteBodyCalibrationSample(
            StreamWriter writer,
            long captureT0,
            long nowMs,
            bool streamReady,
            float[,] filtered,
            float[] rawX,
            float[] rawY,
            float[] rawC)
        {
            string F(float value) => value.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture);
            float KneeAngle(int hip, int knee, int ankle)
            {
                if (filtered[hip, 2] < LegJointMinConfidence
                    || filtered[knee, 2] < LegJointMinConfidence
                    || filtered[ankle, 2] < LegJointMinConfidence)
                    return -1f;
                float ax = filtered[hip, 0] - filtered[knee, 0];
                float ay = filtered[hip, 1] - filtered[knee, 1];
                float bx = filtered[ankle, 0] - filtered[knee, 0];
                float by = filtered[ankle, 1] - filtered[knee, 1];
                float denom = MathF.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                if (denom < 0.00001f) return -1f;
                float cosine = Math.Clamp((ax * bx + ay * by) / denom, -1f, 1f);
                return MathF.Acos(cosine) * (180f / MathF.PI);
            }

            float meanCoreConf = 0f;
            for (int i = KpLSho; i <= KpRAnk; i++) meanCoreConf += filtered[i, 2];
            meanCoreConf /= (KpRAnk - KpLSho + 1);
            float meanFootConf = 0f;
            for (int i = KpLBigToe; i <= KpRHeel; i++) meanFootConf += filtered[i, 2];
            meanFootConf /= (KpRHeel - KpLBigToe + 1);

            var line = new System.Text.StringBuilder(2300);
            line.Append((nowMs - captureT0).ToString(System.Globalization.CultureInfo.InvariantCulture));
            line.Append(';').Append(DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
            line.Append(';').Append(Volatile.Read(ref _bodyCalibrationSegment));
            line.Append(';').Append(_bodyCalibrationView);
            line.Append(';').Append(_bodyCalibrationAction);
            line.Append(';').Append(_bodyCalibrationLeg);
            line.Append(';').Append(Volatile.Read(ref _bodyCalibrationTake));
            line.Append(';').Append(streamReady ? '1' : '0');
            line.Append(';').Append(F(meanCoreConf));
            line.Append(';').Append(F(meanFootConf));
            line.Append(';').Append(F(BodyReferenceHeightM));
            line.Append(';').Append(_bodyFloorValid ? '1' : '0');
            line.Append(';').Append(F(_bodyFloorY));
            line.Append(';').Append(_bodyScaleValid ? '1' : '0');
            line.Append(';').Append(F(_bodyMetersPerUnit));
            line.Append(';').Append(F(KneeAngle(KpLHip, KpLKnee, KpLAnk)));
            line.Append(';').Append(F(KneeAngle(KpRHip, KpRKnee, KpRAnk)));
            line.Append(';').Append(F(_bodyLegStraightness[0]));
            line.Append(';').Append(F(_bodyLegStraightness[1]));
            line.Append(';').Append(F(_bodyKneeDepth[0]));
            line.Append(';').Append(F(_bodyKneeDepth[1]));
            line.Append(';').Append(F(_bodyFootDepth[0]));
            line.Append(';').Append(F(_bodyFootDepth[1]));
            line.Append(';').Append(F(_bodyFootLift[0]));
            line.Append(';').Append(F(_bodyFootLift[1]));
            for (int side = 0; side < 2; side++)
            {
                line.Append(';').Append(_bodyOutputFootValid[side] ? '1' : '0');
                line.Append(';').Append(F(_bodyOutputFootPosition[side, 0]));
                line.Append(';').Append(F(_bodyOutputFootPosition[side, 1]));
                line.Append(';').Append(F(_bodyOutputFootPosition[side, 2]));
                line.Append(';').Append(F(_bodyOutputFootVelocity[side, 0]));
                line.Append(';').Append(F(_bodyOutputFootVelocity[side, 1]));
                line.Append(';').Append(F(_bodyOutputFootVelocity[side, 2]));
            }
            line.Append(';').Append(_bodyOutputArmValid ? '1' : '0');
            line.Append(';').Append(F(_bodyOutputArmFeature[0]));
            line.Append(';').Append(F(_bodyOutputArmFeature[1]));
            line.Append(';').Append(F(_bodyOutputArmPhase));
            line.Append(';').Append(F(_bodyOutputHandY[0]));
            line.Append(';').Append(F(_bodyOutputHandY[1]));
            line.Append(';').Append((int)_bodyLegClassification.Left.State);
            line.Append(';').Append((int)_bodyLegClassification.Right.State);
            line.Append(';').Append((int)_bodyLegClassification.Left.TransportState);
            line.Append(';').Append((int)_bodyLegClassification.Right.TransportState);
            line.Append(';').Append(F(_bodyLegClassification.Left.Confidence));
            line.Append(';').Append(F(_bodyLegClassification.Right.Confidence));
            line.Append(';').Append(_bodyLegClassification.Left.AnatomicalFlipRejected ? '1' : '0');
            line.Append(';').Append(_bodyLegClassification.Right.AnatomicalFlipRejected ? '1' : '0');
            line.Append(';').Append(_bodyLegClassification.CoordinatedGait ? '1' : '0');

            for (int i = KpLSho; i <= KpRHeel; i++)
            {
                line.Append(';').Append(F(filtered[i, 0]));
                line.Append(';').Append(F(filtered[i, 1]));
                line.Append(';').Append(F(filtered[i, 2]));
                line.Append(';').Append(F(rawX[i]));
                line.Append(';').Append(F(rawY[i]));
                line.Append(';').Append(F(rawC[i]));
            }
            writer.WriteLine(line.ToString());
        }

        private void BodyCaptureLoop(string source, int captureGeneration)
        {
            OpenCvSharp.VideoCapture? cap = null;
            StreamWriter? diag = null;
            StreamWriter? calibrationDiag = null;
            World3DCalibrationRecorder? world3dCalibrationRecorder = null;
            Thread? grabberThread = null;
            MediaPipePoseWorker? world3dWorker = null;
            UdpClient? sessionOsc = null;
            string trackingStatusText = "Tracking";

            try
            {
                cap = int.TryParse(source, out int idx)
                    ? new OpenCvSharp.VideoCapture(idx, OpenCvSharp.VideoCaptureAPIs.DSHOW)
                    : new OpenCvSharp.VideoCapture(source);
                string cameraFormatNote = "";
                if (cap.IsOpened() && int.TryParse(source, out _))
                {
                    // Without format hints DSHOW negotiates uncompressed YUY2 at
                    // the camera's maximum resolution, which USB bandwidth caps
                    // at 5-7.5 fps (measured live: 141 ms frame gaps with only
                    // ~47 ms pipeline age). MJPG at 720p keeps webcams at their
                    // full frame rate; MediaPipe downscales internally, so
                    // capture resolution above 720p adds nothing but latency.
                    try
                    {
                        cap.Set(OpenCvSharp.VideoCaptureProperties.FourCC,
                            OpenCvSharp.VideoWriter.FourCC('M', 'J', 'P', 'G'));
                        cap.Set(OpenCvSharp.VideoCaptureProperties.FrameWidth, 1280);
                        cap.Set(OpenCvSharp.VideoCaptureProperties.FrameHeight, 720);
                        cap.Set(OpenCvSharp.VideoCaptureProperties.Fps, 30);
                        double negotiated = cap.Get(OpenCvSharp.VideoCaptureProperties.Fps);
                        if (negotiated > 0 && negotiated < 15)
                        {
                            // The camera refused 30 fps at 720p; USB bandwidth
                            // or driver caps often lift at a lower resolution.
                            cap.Set(OpenCvSharp.VideoCaptureProperties.FrameWidth, 640);
                            cap.Set(OpenCvSharp.VideoCaptureProperties.FrameHeight, 480);
                            cap.Set(OpenCvSharp.VideoCaptureProperties.Fps, 30);
                        }
                        int fcc = (int)cap.Get(OpenCvSharp.VideoCaptureProperties.FourCC);
                        string fourcc = new string(new[]
                        {
                            (char)(fcc & 255), (char)((fcc >> 8) & 255),
                            (char)((fcc >> 16) & 255), (char)((fcc >> 24) & 255),
                        });
                        cameraFormatNote = string.Format(" | cam {0:F0}x{1:F0}@{2:F0} {3}",
                            cap.Get(OpenCvSharp.VideoCaptureProperties.FrameWidth),
                            cap.Get(OpenCvSharp.VideoCaptureProperties.FrameHeight),
                            cap.Get(OpenCvSharp.VideoCaptureProperties.Fps),
                            fourcc);
                    }
                    catch { }
                }
                if (!cap.IsOpened())
                {
                    if (IsBodyCaptureSessionActive(captureGeneration))
                    {
                        BodySetStatus("Camera failed to open: " + source, Color.FromArgb(255, 120, 120));
                        _bodyCapRunning = false;
                        BeginInvoke(() =>
                        {
                            if (Volatile.Read(ref _bodyCaptureGeneration) == captureGeneration)
                                StopBodyCapture();
                        });
                    }
                    return;
                }

                uint captureEpoch = (uint)(Environment.TickCount64
                    % OscTrackerFrameContract.MaximumExactFloatInteger);
                if (captureEpoch == 0) captureEpoch = 1;
                uint world3dSourceEpoch = captureEpoch == OscTrackerFrameContract.MaximumExactFloatInteger
                    ? 1u : captureEpoch + 1u;
                var trackerMux = new BodyTrackerFrameMux();
                trackerMux.Reset(captureEpoch);
                var world3dLegClassifier = new World3DLegMotionClassifier();
                var world3dCaptureDeriver = new World3DLegCaptureDeriver(
                    new World3DLegCaptureDeriverOptions
                    {
                        MinimumLandmarkConfidence = 0.25f,
                    });
                MediaPipePoseWorkerFrameSource? world3dSource = null;
                MediaPipe33TrackerConverter? world3dConverter = null;
                WorldLandmarkFrame? latestWorldLandmarkFrame = null;
                BodyTrackerFrame? latestWorld3dFrame = null;
                CameraLowerBodyClassification latestWorld3dLegClassification = default;
                World3DLegCapturePair latestWorld3dCaptureLegs = default;
                long lastConvertedWorld3dSequence = -1;
                long lastPublishedWorld3dSequence = -1;
                long lastRecordedWorld3dSequence = -1;
                BodyTrackerSource lastPublishedSource = BodyTrackerSource.None;
                bool world3dFaultReported = false;
                bool world3dArmCenterValid = false;
                float world3dArmCenter = 0f;

                if (_bodyUseWorld3D)
                {
                    try
                    {
                        world3dWorker = new MediaPipePoseWorker(
                            new MediaPipePoseLandmarkerOptions
                            {
                                MinimumPoseDetectionConfidence = 0.40f,
                                MinimumPosePresenceConfidence = 0.30f,
                                MinimumTrackingConfidence = 0.30f,
                            });
                        world3dSource = new MediaPipePoseWorkerFrameSource(
                            world3dWorker, world3dSourceEpoch);
                        world3dConverter = new MediaPipe33TrackerConverter(
                            new MediaPipe33TrackerConverterOptions
                            {
                                // Feet lose visibility while aimed into the
                                // camera. Presence and direct 3D geometry stay
                                // useful below the old 2D confidence gate.
                                MinimumLandmarkConfidence = 0.25f,
                                IncludeAuxiliaryTrackers = true,
                            });
                        trackingStatusText = "Tracking (World 3D Lite, MediaPipe CPU)" + cameraFormatNote;
                        BodySetStatus(trackingStatusText, Color.FromArgb(120, 220, 120));
                    }
                    catch (Exception ex)
                    {
                        try { world3dWorker?.Dispose(); } catch { }
                        world3dWorker = null;
                        world3dSource = null;
                        world3dConverter = null;
                        trackingStatusText += " - World 3D unavailable; tracker output paused";
                        BodySetStatus(trackingStatusText + ": " + ex.Message,
                            Color.FromArgb(255, 175, 90));
                    }
                }

                sessionOsc = new UdpClient();
                sessionOsc.Connect(IPAddress.Loopback, _bodyOscPort);
                lock (_bodyOscGate)
                {
                    if (!IsBodyCaptureSessionActive(captureGeneration))
                        return;
                    _bodyOsc = sessionOsc;
                }

                // Anti-lag capture: a dedicated grabber thread keeps ONLY the
                // newest frame. Without this, the capture buffer queues frames
                // whenever inference runs slower than the camera, and the
                // preview drifts seconds behind reality.
                try { cap.Set(OpenCvSharp.VideoCaptureProperties.BufferSize, 1); } catch { }
                OpenCvSharp.Mat? latestFrame = null;
                object latestLock = new object();
                OpenCvSharp.VideoCapture grabberCapture = cap;
                var grabber = new Thread(() =>
                {
                    try
                    {
                        using var g = new OpenCvSharp.Mat();
                        long fpsProbeStartMs = Environment.TickCount64;
                        int fpsProbeFrames = 0;
                        bool exposureOverrideDecided = false;
                        while (IsBodyCaptureSessionActive(captureGeneration))
                        {
                            if (!grabberCapture.Read(g) || g.Empty())
                            {
                                Thread.Sleep(5);
                                continue;
                            }
                            fpsProbeFrames++;
                            long probeElapsed = Environment.TickCount64 - fpsProbeStartMs;
                            if (!exposureOverrideDecided && probeElapsed > 3000)
                            {
                                exposureOverrideDecided = true;
                                double measuredFps = fpsProbeFrames * 1000.0 / probeElapsed;
                                if (measuredFps < 15.0)
                                {
                                    // Delivery is slow despite the negotiated
                                    // format: the usual culprit is low-light
                                    // auto-exposure holding the shutter open
                                    // for 130-200 ms. Cap it at ~1/32 s; the
                                    // image gets darker but arrives on time.
                                    // More room light beats this override.
                                    try
                                    {
                                        grabberCapture.Set(OpenCvSharp.VideoCaptureProperties.AutoExposure, 0.25);
                                        grabberCapture.Set(OpenCvSharp.VideoCaptureProperties.Exposure, -5);
                                        BodySetStatus(string.Format(
                                            "Camera delivered {0:F0} fps - forced 1/32s shutter (low light?). Add room light for best tracking.",
                                            measuredFps), Color.FromArgb(255, 190, 90));
                                    }
                                    catch { }
                                }
                            }
                            var clone = g.Clone();
                            lock (latestLock)
                            {
                                latestFrame?.Dispose();
                                latestFrame = clone;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (IsBodyCaptureSessionActive(captureGeneration))
                            BodySetStatus("Camera read failed: " + ex.Message, Color.FromArgb(255, 120, 120));
                    }
                    finally
                    {
                        lock (latestLock)
                        {
                            latestFrame?.Dispose();
                            latestFrame = null;
                        }
                        try { grabberCapture.Release(); grabberCapture.Dispose(); } catch { }
                    }
                })
                { IsBackground = true };
                grabberThread = grabber;
                grabber.Start();
                cap = null; // ownership moved to the grabber thread

                using var resized = new OpenCvSharp.Mat();
                using var world3dScaled = new OpenCvSharp.Mat();
                using var world3dRgb = new OpenCvSharp.Mat();
                // Keep the dormant Legacy2D implementation source-compatible
                // without allocating any of its pose/filter buffers in World3D.
                var kp = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount, 3] : new float[0, 0];
                var prevKp = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount, 3] : new float[0, 0];
                var posePrimary = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount * 3] : Array.Empty<float>();
                var poseSecondary = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount * 3] : Array.Empty<float>();
                bool havePrev = false;
                float lastSpan = 0.5f; // normalized body span, for the speed ceiling
                long lastPoseFilterMs = 0;
                int fullBodyMissFrames = 0;
                int fullBodyGoodFrames = 0;
                bool bodyStreamReady = false;

                // Motion diagnostics: per-frame raw jump, filtered jump and
                // mirror-pass disagreement for key joints, so jitter can be
                // measured instead of argued about. Overwritten each Start.
                var abDisagree = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                var rawStep = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                // Per-joint EMA of half the mirror-pass gap: keeps single-pass
                // fallback centered instead of lurching across the full gap.
                var abHalfGapX = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                var abHalfGapY = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                var rawKx = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                var rawKy = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                var rawKc = !_bodyUseWorld3D
                    ? new float[TrackedKeypointCount] : Array.Empty<float>();
                long world3dDiagLastWriteMs = 0;
                long world3dDiagLastFlushMs = 0;
                long calibrationT0 = 0;
                long calibrationLastWriteMs = 0;
                long calibrationLastFlushMs = 0;
                bool calibrationWasRecording = false;
                long lastInferenceStartMs = 0;
                try
                {
                    string diagnosticDir = Path.Combine(AppContext.BaseDirectory, "BodyTracking");
                    Directory.CreateDirectory(diagnosticDir);
                    diag = new StreamWriter(
                        Path.Combine(diagnosticDir, "world3d_diag.csv"), false);
                    diag.WriteLine(
                        "harvest_now_ms;sequence;frame_timestamp_ms;harvest_age_ms;source_epoch;active;core_valid;" +
                        "l_state;l_confidence;r_state;r_confidence;" +
                        "waist_x;waist_y;waist_z;lfoot_x;lfoot_y;lfoot_z;rfoot_x;rfoot_y;rfoot_z;" +
                        "lhip_x;lhip_y;lhip_z;lhip_conf;lknee_x;lknee_y;lknee_z;lknee_conf;" +
                        "lankle_x;lankle_y;lankle_z;lankle_conf;" +
                        "rhip_x;rhip_y;rhip_z;rhip_conf;rknee_x;rknee_y;rknee_z;rknee_conf;" +
                        "rankle_x;rankle_y;rankle_z;rankle_conf");
                    diag.Flush();
                }
                catch { }

                while (IsBodyCaptureSessionActive(captureGeneration))
                {
                    // The source is at most 30 useful frames per second. Let the
                    // grabber keep replacing its one-frame slot while we wait,
                    // then infer the newest image instead of processing duplicates.
                    long inferenceNow = Environment.TickCount64;
                    int inferenceWait = (int)(BodyInferenceIntervalMs - (inferenceNow - lastInferenceStartMs));
                    if (lastInferenceStartMs != 0 && inferenceWait > 0)
                    {
                        Thread.Sleep(inferenceWait);
                        continue;
                    }

                    // Take the newest frame the grabber has; never a queued one
                    OpenCvSharp.Mat? take = null;
                    lock (latestLock)
                    {
                        if (latestFrame != null) { take = latestFrame; latestFrame = null; }
                    }
                    if (take == null) { Thread.Sleep(5); continue; }
                    using var frame = take;
                    if (frame.Empty()) { Thread.Sleep(5); continue; }
                    lastInferenceStartMs = Environment.TickCount64;
                    // NOTE: inference always runs on the RAW frame; the Mirror
                    // checkbox only flips the final preview bitmap. (Inferring
                    // on a pre-mirrored frame shifted the model's placement.)

                    if (world3dWorker != null && world3dSource != null)
                    {
                        try
                        {
                            const int maxWorld3dInputEdge = 960;
                            OpenCvSharp.Mat worldInput = frame;
                            int longestEdge = Math.Max(frame.Width, frame.Height);
                            if (longestEdge > maxWorld3dInputEdge)
                            {
                                float scale = maxWorld3dInputEdge / (float)longestEdge;
                                int width = Math.Max(1, (int)MathF.Round(frame.Width * scale));
                                int height = Math.Max(1, (int)MathF.Round(frame.Height * scale));
                                OpenCvSharp.Cv2.Resize(frame, world3dScaled,
                                    new OpenCvSharp.Size(width, height),
                                    0, 0, OpenCvSharp.InterpolationFlags.Area);
                                worldInput = world3dScaled;
                            }

                            OpenCvSharp.ColorConversionCodes conversion = worldInput.Channels() switch
                            {
                                1 => OpenCvSharp.ColorConversionCodes.GRAY2RGB,
                                4 => OpenCvSharp.ColorConversionCodes.BGRA2RGB,
                                _ => OpenCvSharp.ColorConversionCodes.BGR2RGB,
                            };
                            OpenCvSharp.Cv2.CvtColor(worldInput, world3dRgb, conversion);
                            world3dSource.UpdateInputMetadata(
                                world3dRgb.Width, world3dRgb.Height, inputMirrored: false);
                            world3dWorker.TrySubmitRgb24(
                                world3dRgb.Data,
                                world3dRgb.Width,
                                world3dRgb.Height,
                                checked((int)world3dRgb.Step()),
                                lastInferenceStartMs);
                        }
                        catch (Exception ex)
                        {
                            if (!world3dFaultReported)
                            {
                                world3dFaultReported = true;
                                BodySetStatus("World 3D input failed; tracker output paused: " + ex.Message,
                                    Color.FromArgb(255, 175, 90));
                            }
                        }
                    }

                    bool poseOk = false;
#if OCU_LEGACY_RTMP
                    if (!_bodyUseWorld3D && pose != null)
                    {
                        try
                        {
                            // Aspect-correct model input. Feeding a squished
                            // portrait frame skews every joint; instead run on
                            // a SQUARE region: a crop tracking the previous
                            // pose when we have one (person fills the input =
                            // far steadier joints, the official MoveNet
                            // recipe), else the letterboxed full frame.
                            int fw = frame.Width, fh = frame.Height;
                            float ox, oy, sx, sy;
                            OpenCvSharp.Mat square;

                            int confPrev = 0;
                            float minX = 1, minY = 1, maxX = 0, maxY = 0;
                            if (havePrev)
                            {
                                // Skip the whole face (0-4, nose/eyes/ears): a
                                // VR headset makes the model hallucinate them,
                                // and facing the camera dead-on it can't even
                                // decide which side the ears are on. They must
                                // never steer the tracking box.
                                for (int i = 0; i < BodyFootKeypointCount; i++)
                                {
                                    if (i <= 4) continue;
                                    if (prevKp[i, 2] < 0.3f) continue;
                                    confPrev++;
                                    minX = Math.Min(minX, prevKp[i, 0]); maxX = Math.Max(maxX, prevKp[i, 0]);
                                    minY = Math.Min(minY, prevKp[i, 1]); maxY = Math.Max(maxY, prevKp[i, 1]);
                                }
                            }

                            // Region aspect matches the MODEL input (square for
                            // MoveNet, portrait 3:4 for RTMW) so resize never
                            // distorts the person.
                            float ar = poseW / (float)poseH;
                            if (confPrev >= 6)
                            {
                                float cxN = (minX + maxX) / 2f, cyN = (minY + maxY) / 2f;
                                float needH = Math.Max((maxY - minY) * fh, (maxX - minX) * fw / ar) * 1.9f;
                                int cH = (int)Math.Clamp(needH, 0.25f * fh, Math.Min(fh, fw / ar));
                                int cW = Math.Min(fw, (int)(cH * ar));
                                int rx = Math.Clamp((int)(cxN * fw - cW / 2f), 0, fw - cW);
                                int ry = Math.Clamp((int)(cyN * fh - cH / 2f), 0, fh - cH);
                                square = new OpenCvSharp.Mat(frame, new OpenCvSharp.Rect(rx, ry, cW, cH));
                                ox = rx / (float)fw; oy = ry / (float)fh;
                                sx = cW / (float)fw; sy = cH / (float)fh;
                            }
                            else
                            {
                                // Pad the full frame out to the model aspect
                                int tW, tH;
                                if (fw / (float)fh > ar) { tW = fw; tH = (int)(fw / ar); }
                                else { tH = fh; tW = (int)(fh * ar); }
                                int padX = (tW - fw) / 2, padY = (tH - fh) / 2;
                                square = new OpenCvSharp.Mat();
                                OpenCvSharp.Cv2.CopyMakeBorder(frame, square, padY, tH - fh - padY, padX, tW - fw - padX,
                                    OpenCvSharp.BorderTypes.Constant, OpenCvSharp.Scalar.Black);
                                ox = -padX / (float)fw; oy = -padY / (float)fh;
                                sx = tW / (float)fw; sy = tH / (float)fh;
                            }

                            using (square)
                                OpenCvSharp.Cv2.Resize(square, resized, new OpenCvSharp.Size(poseW, poseH));
                            // Dual-flip inference: run the model on the square
                            // AND its mirror, un-mirror the second result, and
                            // confidence-average the pair. MoveNet carries a
                            // horizontal placement bias whose sign flips under
                            // mirroring (measured ~10% of frame width on a live
                            // scene), so the average cancels it. Left/right
                            // joint labels swap in the mirrored pass and are
                            // swapped back via KpMirror.
                            bool outTOk = poseBuffers != null
                                && RunPoseOnSquare(pose, resized, poseBuffers, posePrimary);
                            var outT = posePrimary;
                            // Dual-flip averaging is a MoveNet crutch (cancels
                            // its mirror bias). RTMW is flip-trained: no bias,
                            // so one pass = same accuracy at twice the fps.
                            int decodedCount = poseRtm ? TrackedKeypointCount : CocoBodyKeypointCount;
                            if (!poseRtm && outTOk)
                            {
                                using var flippedSq = new OpenCvSharp.Mat();
                                OpenCvSharp.Cv2.Flip(resized, flippedSq, OpenCvSharp.FlipMode.Y);
                                bool outBOk = poseBuffers != null
                                    && RunPoseOnSquare(pose, flippedSq, poseBuffers, poseSecondary);
                                var outB = poseSecondary;
                                if (outBOk)
                                {
                                    for (int i = 0; i < CocoBodyKeypointCount; i++)
                                    {
                                        int j = KpMirror[i];
                                        float ya = outT[i * 3 + 0], xa = outT[i * 3 + 1], ca = outT[i * 3 + 2];
                                        float yb = outB[j * 3 + 0], xb = 1f - outB[j * 3 + 1], cb = outB[j * 3 + 2];
                                        abDisagree[i] = (ca > 0.2f && cb > 0.2f)
                                            ? MathF.Sqrt((xa - xb) * (xa - xb) + (ya - yb) * (ya - yb))
                                            : -1f;
                                        // MEASURED (motion_diag): the passes sit
                                        // ~10% of frame apart, stably. The old
                                        // confidence-WEIGHTED average toggled
                                        // between A / B / midpoint as weights
                                        // fluctuated = 10%-of-frame teleports
                                        // while standing still. Fix: fixed 50/50
                                        // center, and single-pass fallback is
                                        // corrected by the remembered half-gap
                                        // so the center never lurches.
                                        if (ca > 0.2f && cb > 0.2f)
                                        {
                                            abHalfGapX[i] += 0.05f * ((xb - xa) / 2f - abHalfGapX[i]);
                                            abHalfGapY[i] += 0.05f * ((yb - ya) / 2f - abHalfGapY[i]);
                                            outT[i * 3 + 0] = (ya + yb) / 2f;
                                            outT[i * 3 + 1] = (xa + xb) / 2f;
                                            outT[i * 3 + 2] = (ca + cb) / 2f;
                                        }
                                        else if (cb > ca)
                                        {
                                            outT[i * 3 + 0] = yb - abHalfGapY[i];
                                            outT[i * 3 + 1] = xb - abHalfGapX[i];
                                            outT[i * 3 + 2] = cb * 0.9f;
                                        }
                                        else
                                        {
                                            outT[i * 3 + 0] = ya + abHalfGapY[i];
                                            outT[i * 3 + 1] = xa + abHalfGapX[i];
                                            outT[i * 3 + 2] = ca * 0.9f;
                                        }
                                    }
                                }
                            }
                            if (outTOk)
                            {
                                // Inference may run at ~10 FPS on CPU or 30+ FPS
                                // on a spare GPU. Normalize the motion ceiling and
                                // filter response to real elapsed time so changing
                                // devices cannot change the resulting body motion.
                                long poseFilterNowMs = Environment.TickCount64;
                                float poseFilterDt = lastPoseFilterMs != 0
                                    ? Math.Clamp((poseFilterNowMs - lastPoseFilterMs) / 1000f, 1f / 90f, 0.15f)
                                    : 1f / 30f;
                                lastPoseFilterMs = poseFilterNowMs;

                                Array.Fill(rawStep, -1f);

                                // Map model coords (square-relative) to
                                // full-frame normalized, all joints first.
                                Array.Clear(rawKc);
                                for (int i = 0; i < decodedCount; i++)
                                {
                                    rawKx[i] = ox + outT[i * 3 + 1] * sx;
                                    rawKy[i] = oy + outT[i * 3 + 0] * sy;
                                    rawKc[i] = outT[i * 3 + 2];
                                }

                                // ANTI-FLIP GUARD (the standard remedy for the
                                // model swapping left/right labels, which reads
                                // as the torso twisting around): if matching
                                // this frame's pair CROSSED to last frame beats
                                // the direct match by a clear margin, the
                                // labels flipped — swap them back.
                                // MoveNet ONLY: RTMW's labels are reliable, and
                                // the guard's hysteresis can LATCH a swap when
                                // you turn around = stuck criss-crossed limbs.
                                if (havePrev && !poseRtm)
                                {
                                    foreach (var (L, R) in KpPairs)
                                    {
                                        if (L >= decodedCount || R >= decodedCount) continue;
                                        if (rawKc[L] < 0.3f || rawKc[R] < 0.3f
                                            || prevKp[L, 2] < 0.3f || prevKp[R, 2] < 0.3f)
                                            continue;
                                        float DistSq(float ax, float ay, int p)
                                        {
                                            float ddx = ax - prevKp[p, 0], ddy = ay - prevKp[p, 1];
                                            return ddx * ddx + ddy * ddy;
                                        }
                                        float keep = DistSq(rawKx[L], rawKy[L], L) + DistSq(rawKx[R], rawKy[R], R);
                                        float swap = DistSq(rawKx[L], rawKy[L], R) + DistSq(rawKx[R], rawKy[R], L);
                                        if (swap < keep * 0.7f)
                                        {
                                            (rawKx[L], rawKx[R]) = (rawKx[R], rawKx[L]);
                                            (rawKy[L], rawKy[R]) = (rawKy[R], rawKy[L]);
                                            (rawKc[L], rawKc[R]) = (rawKc[R], rawKc[L]);
                                        }
                                    }
                                }

                                for (int i = 0; i < TrackedKeypointCount; i++)
                                {
                                    float fxN = rawKx[i];
                                    float fyN = rawKy[i];
                                    float conf = rawKc[i];
                                    bool weakLegMeasurement = poseRtm && i >= KpLKnee && i <= KpRHeel;
                                    float confidenceGate = weakLegMeasurement
                                        ? LegJointMinConfidence
                                        : BodyJointMinConfidence;

                                    if (havePrev)
                                    {
                                        if (conf >= confidenceGate)
                                        {
                                            float dx = fxN - prevKp[i, 0], dy = fyN - prevKp[i, 1];
                                            float dist = MathF.Sqrt(dx * dx + dy * dy);
                                            rawStep[i] = dist; // diagnostic: pre-filter jump

                                            // HARD HUMAN-SPEED CEILING: a fast
                                            // kick peaks ~8 m/s ≈ 20% of body
                                            // span per 30fps tick. Anything
                                            // faster is physically impossible —
                                            // clamp the step, never pass it.
                                            //
                                            // ARM JOINTS (shoulders/elbows/
                                            // wrists, 5-10) get half the ceiling
                                            // and heavy damping: MoveNet's arm
                                            // detection is its noisiest (worse
                                            // with controllers in hand), and in
                                            // VR the controllers track the arms
                                            // anyway — camera arms only feed the
                                            // preview + slow alignment refs.
                                            // Extra arm damping is a MoveNet
                                            // crutch (wild wrists); RTMW's arms
                                            // are clean — full responsiveness.
                                            bool armJoint = !poseRtm && i >= 5 && i <= 10;
                                            bool legJoint = i >= KpLKnee && i <= KpRHeel;
                                            float spansPerSecond = armJoint ? 3.0f : legJoint ? 7.0f : 6.0f;
                                            float maxStep = Math.Clamp(spansPerSecond * lastSpan * poseFilterDt,
                                                armJoint ? 0.015f : 0.025f,
                                                legJoint ? 0.18f : armJoint ? 0.07f : 0.12f);
                                            if (dist > maxStep)
                                            {
                                                float k = maxStep / dist;
                                                dx *= k;
                                                dy *= k;
                                                dist = maxStep;
                                            }

                                            // Damping: calm when still, quicker
                                            // when moving, but never raw.
                                            float nominalAlpha = armJoint ? 0.12f : legJoint ? 0.24f : 0.20f;
                                            float baseAlpha = 1f - MathF.Pow(1f - nominalAlpha, poseFilterDt * 30f);
                                            float motionGain = armJoint ? 4f : legJoint ? 10f : 8f;
                                            float maxAlpha = armJoint ? 0.48f : legJoint ? 0.92f : 0.85f;
                                            float alpha = Math.Clamp(baseAlpha + dist * motionGain, baseAlpha, maxAlpha);
                                            if (weakLegMeasurement && conf < BodyJointMinConfidence)
                                            {
                                                // Low-score leg landmarks are useful but noisy. Keep
                                                // their motion, constrained by the human-speed ceiling,
                                                // with response proportional to the model's certainty.
                                                float trust = Math.Clamp((conf - LegJointMinConfidence) /
                                                    (BodyJointMinConfidence - LegJointMinConfidence), 0f, 1f);
                                                alpha = baseAlpha + (alpha - baseAlpha) * (0.35f + 0.65f * trust);
                                            }
                                            fxN = prevKp[i, 0] + alpha * dx;
                                            fyN = prevKp[i, 1] + alpha * dy;
                                        }
                                        else
                                        {
                                            // Joint lost this frame: hold the
                                            // cached position and decay its
                                            // confidence instead of letting raw
                                            // low-conf guesses (or the re-lock
                                            // frame) slam through unfiltered.
                                            fxN = prevKp[i, 0];
                                            fyN = prevKp[i, 1];
                                            float decay = MathF.Pow(0.93f, poseFilterDt * 30f);
                                            conf = prevKp[i, 2] * decay;
                                        }
                                    }

                                    kp[i, 0] = fxN;
                                    kp[i, 1] = fyN;
                                    kp[i, 2] = conf;
                                }
                                poseOk = true;

                                // Crop tracking is allowed only while the RAW
                                // model can still see the complete body. The
                                // old path treated "wrists still visible" as a
                                // valid pose, kept an upper-body crop forever,
                                // and silently stopped emitting both feet.
                                // Four misses reject a transient occlusion;
                                // three clean full-frame reads are required
                                // before virtual trackers resume on a freshly
                                // calibrated coordinate frame.
                                float lowerBodyGate = poseRtm ? LegJointMinConfidence : BodyJointMinConfidence;
                                bool skeletonCoreVisible = rawKc[KpLSho] >= BodyJointMinConfidence
                                    && rawKc[KpRSho] >= BodyJointMinConfidence
                                    && rawKc[KpLHip] >= BodyJointMinConfidence
                                    && rawKc[KpRHip] >= BodyJointMinConfidence
                                    && rawKc[KpLKnee] >= lowerBodyGate
                                    && rawKc[KpRKnee] >= lowerBodyGate;
                                bool leftFootVisible = rawKc[KpLAnk] >= lowerBodyGate
                                    || rawKc[KpLHeel] >= lowerBodyGate
                                    || rawKc[KpLBigToe] >= lowerBodyGate
                                    || rawKc[KpLSmallToe] >= lowerBodyGate;
                                bool rightFootVisible = rawKc[KpRAnk] >= lowerBodyGate
                                    || rawKc[KpRHeel] >= lowerBodyGate
                                    || rawKc[KpRBigToe] >= lowerBodyGate
                                    || rawKc[KpRSmallToe] >= lowerBodyGate;
                                if (skeletonCoreVisible)
                                {
                                    if (bodyStreamReady)
                                    {
                                        // Once the floor/body frame is locked,
                                        // a weak ankle is a per-leg event. Do not
                                        // throw away both legs and recalibrate.
                                        fullBodyMissFrames = 0;
                                    }
                                    else if (leftFootVisible && rightFootVisible)
                                    {
                                        fullBodyMissFrames = 0;
                                        fullBodyGoodFrames++;
                                    }
                                    else
                                    {
                                        // Before initial lock, periodically fall
                                        // back to the full camera frame so an
                                        // upper-body crop cannot hide the feet
                                        // forever.
                                        fullBodyGoodFrames = 0;
                                        fullBodyMissFrames++;
                                        if (fullBodyMissFrames == 4)
                                        {
                                            ResetBodyCoordinateCalibration();
                                            BodySetStatus("Feet not locked - step back into full camera frame", Color.FromArgb(255, 170, 80));
                                        }
                                    }
                                    if (!bodyStreamReady && fullBodyGoodFrames >= 3)
                                    {
                                        bodyStreamReady = true;
                                        BodySetStatus(trackingStatusText, Color.FromArgb(120, 220, 120));
                                    }
                                }
                                else
                                {
                                    fullBodyGoodFrames = 0;
                                    fullBodyMissFrames++;
                                    if (fullBodyMissFrames == 4)
                                    {
                                        bodyStreamReady = false;
                                        ResetBodyCoordinateCalibration();
                                        BodySetStatus("Feet/body lost - step back into full camera frame", Color.FromArgb(255, 170, 80));
                                    }
                                }

                                Array.Copy(kp, prevKp, kp.Length);
                                // After sustained lower-body loss, inference
                                // stays on the full camera frame until the
                                // complete skeleton is reacquired. This breaks
                                // the stale upper-body-crop feedback loop.
                                havePrev = fullBodyMissFrames < 4;
                                lastSpan = EstimateSpan(kp, lastSpan);

                                // Manual nudge AFTER the tracker state is
                                // stored: applying it inside the smoothing/
                                // crop feedback would compound it each frame.
                                float nx = _bodyOffX / 100f, ny = _bodyOffY / 100f;
                                if (nx != 0f || ny != 0f)
                                {
                                    for (int i = 0; i < TrackedKeypointCount; i++)
                                    {
                                        kp[i, 0] += nx;
                                        kp[i, 1] += ny;
                                    }
                                }
                            }
                        }
                        catch { poseOk = false; }
                    }
#endif
                    if (!poseOk)
                    {
                        havePrev = false; // reset the crop tracker so we don't chase a stale box
                        lastPoseFilterMs = 0;
                        fullBodyMissFrames = 4;
                        fullBodyGoodFrames = 0;
                        if (bodyStreamReady)
                            ResetBodyCoordinateCalibration();
                        bodyStreamReady = false;
                    }

                    // Stop/restart may happen while native inference is blocked.
                    // Never let an obsolete session publish into the new one.
                    if (!IsBodyCaptureSessionActive(captureGeneration))
                        break;

                    long trackerNowMs = Environment.TickCount64;
                    if (world3dSource != null && world3dConverter != null
                        && world3dSource.TryGetLatestFrame(out WorldLandmarkFrame worldLandmarks)
                        && worldLandmarks.Sequence != lastConvertedWorld3dSequence)
                    {
                        try
                        {
                            latestWorldLandmarkFrame = worldLandmarks;
                            latestWorld3dFrame = world3dConverter.Convert(worldLandmarks);
                            latestWorld3dLegClassification = world3dLegClassifier.Update(
                                worldLandmarks,
                                latestWorld3dFrame);
                            latestWorld3dCaptureLegs = world3dCaptureDeriver.Derive(
                                worldLandmarks,
                                latestWorld3dLegClassification,
                                "world3d");
                            lastConvertedWorld3dSequence = worldLandmarks.Sequence;
                        }
                        catch (Exception ex)
                        {
                            latestWorldLandmarkFrame = null;
                            latestWorld3dFrame = null;
                            latestWorld3dLegClassification = default;
                            latestWorld3dCaptureLegs = default;
                            world3dLegClassifier.Reset();
                            world3dCaptureDeriver.Reset();
                            if (!world3dFaultReported)
                            {
                                world3dFaultReported = true;
                                BodySetStatus("World 3D conversion failed; tracker output paused: " + ex.Message,
                                    Color.FromArgb(255, 175, 90));
                            }
                        }
                    }
                    if (world3dWorker?.LastError is Exception workerError
                        && !world3dFaultReported)
                    {
                        world3dFaultReported = true;
                        BodySetStatus("World 3D worker failed; tracker output paused: " + workerError.Message,
                            Color.FromArgb(255, 175, 90));
                    }

                    // MediaPipe is the only selectable source. A dropout is an
                    // explicit tracker loss; Legacy2D never receives a marker
                    // and can never enter the mux as fallback.
                    bool haveTrackerSelection = trackerMux.TrySelect(
                        trackerNowMs,
                        latestWorld3dFrame,
                        null,
                        out BodyTrackerMuxSelection trackerSelection);
                    Volatile.Write(ref _bodyActiveTrackerSource,
                        (int)(haveTrackerSelection
                            ? trackerSelection.Source
                            : BodyTrackerSource.None));
                    bool continuousSelected = haveTrackerSelection
                        && trackerSelection.Source == BodyTrackerSource.Continuous3D;
                    Vector3 continuousGaitArms = Vector3.Zero;
                    bool continuousArmsAvailable = continuousSelected
                        && latestWorldLandmarkFrame != null
                        && latestWorldLandmarkFrame.Sequence == trackerSelection.Frame.Sequence
                        && TryBuildContinuousGaitArms(
                            latestWorldLandmarkFrame,
                            trackerSelection.Frame,
                            out continuousGaitArms);
                    bool continuousLegSemanticsAvailable = continuousSelected
                        && latestWorldLandmarkFrame != null
                        && latestWorldLandmarkFrame.Sequence == trackerSelection.Frame.Sequence
                        && latestWorldLandmarkFrame.SourceEpoch == trackerSelection.Frame.SourceEpoch
                        && latestWorld3dLegClassification.TimestampMs
                            == trackerSelection.Frame.TimestampMs;
                    CameraLowerBodyClassification selectedWorld3dLegClassification =
                        continuousLegSemanticsAvailable
                            ? latestWorld3dLegClassification
                            : default;

                    _bodyTrackingStreamReady = continuousSelected;

                    // Dormant migration path only. The immutable World3D mode
                    // makes this false, so the Legacy2D solver and OSC sender
                    // cannot run alongside MediaPipe.
                    if (!_bodyUseWorld3D && poseOk && bodyStreamReady
                        && (_bodyStreamEnabled || _bodyCalibrationRecording))
                    {
                        SendBodyOsc(
                            kp,
                            frame.Width,
                            frame.Height,
                            _bodyStreamEnabled,
                            publishLegacyTrackers: haveTrackerSelection
                                && trackerSelection.Source == BodyTrackerSource.Legacy2D,
                            sourceEpoch: haveTrackerSelection
                                ? trackerSelection.OutputSourceEpoch : captureEpoch,
                            publishGaitArms: haveTrackerSelection
                                && trackerSelection.Source == BodyTrackerSource.Legacy2D,
                            publishGaitLegSemantics: haveTrackerSelection
                                && trackerSelection.Source == BodyTrackerSource.Legacy2D);
                    }

                    if (_bodyStreamEnabled)
                    {
                        if (continuousSelected)
                        {
                            bool publishWorldFrame = trackerSelection.SourceChanged
                                || trackerSelection.Frame.Sequence != lastPublishedWorld3dSequence;
                            if (publishWorldFrame)
                            {
                                if (continuousArmsAvailable)
                                {
                                    if (!world3dArmCenterValid || trackerSelection.SourceChanged)
                                    {
                                        world3dArmCenter = continuousGaitArms.X;
                                        world3dArmCenterValid = true;
                                    }
                                    else
                                    {
                                        world3dArmCenter += 0.004f
                                            * (continuousGaitArms.X - world3dArmCenter);
                                    }
                                    continuousGaitArms.X = Math.Clamp(
                                        continuousGaitArms.X - world3dArmCenter,
                                        -0.40f,
                                        0.40f);
                                }
                                SendRawMediaPipeFramePacket(
                                    latestWorldLandmarkFrame!,
                                    trackerSelection.OutputSourceEpoch,
                                    continuousArmsAvailable ? continuousGaitArms : null,
                                    selectedWorld3dLegClassification);
                                lastPublishedWorld3dSequence = trackerSelection.Frame.Sequence;
                            }
                            lastPublishedSource = BodyTrackerSource.Continuous3D;
                        }
                        else
                        {
                            if (lastPublishedSource != BodyTrackerSource.None)
                                SendEmptyBodyTrackerFrame(trackerMux.OutputSourceEpoch);
                            lastPublishedSource = BodyTrackerSource.None;
                            lastPublishedWorld3dSequence = -1;
                            world3dArmCenterValid = false;
                        }
                    }
                    else
                    {
                        if (lastPublishedSource != BodyTrackerSource.None)
                            SendEmptyBodyTrackerFrame(trackerMux.OutputSourceEpoch);
                        // A later re-enable must republish even if the worker's
                        // latest sequence has not changed yet.
                        lastPublishedSource = BodyTrackerSource.None;
                        lastPublishedWorld3dSequence = -1;
                    }

                    UpdateMediaPipeSilhouetteState(
                        continuousSelected
                            && latestWorldLandmarkFrame != null
                            && latestWorldLandmarkFrame.Sequence == trackerSelection.Frame.Sequence
                                ? latestWorldLandmarkFrame
                                : null,
                        continuousSelected);

                    if (diag != null
                        && trackerNowMs - world3dDiagLastWriteMs >= 100)
                    {
                        try
                        {
                            world3dDiagLastWriteMs = trackerNowMs;
                            WriteWorld3dDiagnosticRow(
                                diag,
                                trackerNowMs,
                                continuousSelected,
                                latestWorldLandmarkFrame,
                                latestWorld3dFrame,
                                latestWorld3dLegClassification);
                            if (trackerNowMs - world3dDiagLastFlushMs >= 1000)
                            {
                                world3dDiagLastFlushMs = trackerNowMs;
                                diag.Flush();
                            }
                        }
                        catch { }
                    }

                    long calibrationNowMs = Environment.TickCount64;
                    if (_bodyCalibrationRecording)
                    {
                        try
                        {
                            if (_bodyUseWorld3D)
                            {
                                WorldLandmarkFrame? captureLandmarks = latestWorldLandmarkFrame;
                                BodyTrackerFrame? captureTrackers = latestWorld3dFrame;
                                if (continuousSelected
                                    && captureLandmarks != null
                                    && captureTrackers != null
                                    && captureTrackers.CorePoseValid
                                    && trackerSelection.Frame.Sequence == captureLandmarks.Sequence
                                    && trackerSelection.Frame.SourceEpoch == captureLandmarks.SourceEpoch
                                    && trackerSelection.Frame.TimestampMs == captureLandmarks.TimestampMs
                                    && captureLandmarks.Sequence != lastRecordedWorld3dSequence
                                    && lastConvertedWorld3dSequence == captureLandmarks.Sequence)
                                {
                                    if (world3dCalibrationRecorder == null)
                                    {
                                        string captureDir = Path.Combine(
                                            AppContext.BaseDirectory,
                                            "BodyTracking",
                                            "Captures");
                                        string fileName =
                                            $"fbt_world3d_capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.csv";
                                        world3dCalibrationRecorder = new World3DCalibrationRecorder();
                                        world3dCalibrationRecorder.Start(
                                            Path.Combine(captureDir, fileName));
                                        _bodyCalibrationFileName = fileName;
                                        BodySetCaptureStatus(
                                            $"RECORDING World3D to BodyTracking\\Captures\\{fileName}",
                                            Color.FromArgb(255, 135, 155));
                                    }

                                    var labels = new World3DCalibrationLabels(
                                        Volatile.Read(ref _bodyCalibrationSegment),
                                        _bodyCalibrationView,
                                        _bodyCalibrationAction,
                                        _bodyCalibrationLeg,
                                        Volatile.Read(ref _bodyCalibrationTake));
                                    BodyTrackerFrame? synchronizedTrackers = captureTrackers != null
                                        && captureTrackers.Source == BodyTrackerSource.Continuous3D
                                        && captureTrackers.Sequence == captureLandmarks.Sequence
                                        && captureTrackers.SourceEpoch == captureLandmarks.SourceEpoch
                                        && captureTrackers.TimestampMs == captureLandmarks.TimestampMs
                                        ? captureTrackers
                                        : null;
                                    world3dCalibrationRecorder.Append(
                                        labels,
                                        captureLandmarks,
                                        synchronizedTrackers,
                                        latestWorld3dCaptureLegs.Left,
                                        latestWorld3dCaptureLegs.Right);
                                    lastRecordedWorld3dSequence = captureLandmarks.Sequence;
                                }
                            }
                            else if (poseOk)
                            {
                                if (calibrationDiag == null)
                                {
                                    string captureDir = Path.Combine(AppContext.BaseDirectory, "BodyTracking", "Captures");
                                    Directory.CreateDirectory(captureDir);
                                    string fileName = $"fbt_capture_{DateTime.Now:yyyyMMdd_HHmmss_fff}.csv";
                                    calibrationDiag = new StreamWriter(Path.Combine(captureDir, fileName), false)
                                    {
                                        // A stopped take is durable even if pose
                                        // is lost before the next inference frame.
                                        AutoFlush = true,
                                    };
                                    WriteBodyCalibrationHeader(calibrationDiag);
                                    calibrationT0 = calibrationNowMs;
                                    calibrationLastWriteMs = 0;
                                    calibrationLastFlushMs = calibrationNowMs;
                                    _bodyCalibrationFileName = fileName;
                                    BodySetCaptureStatus(
                                        $"RECORDING Legacy2D to BodyTracking\\Captures\\{fileName}",
                                        Color.FromArgb(255, 135, 155));
                                }

                                // One row per pose inference frame preserves the
                                // 100-250 ms chamber-to-extension transition that
                                // the old 10 Hz motion diagnostic could miss.
                                if (calibrationNowMs - calibrationLastWriteMs >= 20)
                                {
                                    calibrationLastWriteMs = calibrationNowMs;
                                    WriteBodyCalibrationSample(
                                        calibrationDiag, calibrationT0, calibrationNowMs,
                                        bodyStreamReady, kp, rawKx, rawKy, rawKc);
                                }
                                if (calibrationNowMs - calibrationLastFlushMs >= 1000)
                                {
                                    calibrationLastFlushMs = calibrationNowMs;
                                    calibrationDiag.Flush();
                                }
                            }
                            calibrationWasRecording = true;
                        }
                        catch (Exception ex)
                        {
                            _bodyCalibrationRecording = false;
                            try { calibrationDiag?.Dispose(); } catch { }
                            calibrationDiag = null;
                            try { world3dCalibrationRecorder?.Dispose(); } catch { }
                            world3dCalibrationRecorder = null;
                            string error = "Calibration capture failed: " + ex.Message;
                            try { BeginInvoke(() => StopBodyCalibrationRecord(error)); }
                            catch
                            {
                                BodySetCaptureStatus(error, Color.FromArgb(255, 120, 120));
                            }
                        }
                    }
                    else if (calibrationWasRecording)
                    {
                        try { calibrationDiag?.Flush(); } catch { }
                        calibrationWasRecording = false;
                    }

                    // Preview is display-only. Skip all bitmap work when the user
                    // turns it off, leaves this page, or minimizes the window.
                    // A single-flight gate also prevents UI callbacks piling up.
                    long previewNow = Environment.TickCount64;
                    long previewLast = Interlocked.Read(ref _bodyPreviewLastFrameMs);
                    if (_bodyPreviewActive
                        && previewNow - previewLast >= BodyPreviewIntervalMs
                        && Interlocked.CompareExchange(ref _bodyPreviewUiPending, 1, 0) == 0)
                    {
                        Interlocked.Exchange(ref _bodyPreviewLastFrameMs, previewNow);
                        int previewModeVersion = Volatile.Read(ref _bodyPreviewModeVersion);
                        bool skeletonOnly = _bodyPreviewSkeletonOnly;
                        Bitmap? bmp = null;
                        try
                        {
                            int longestEdge = Math.Max(frame.Width, frame.Height);
                            float previewScale = longestEdge > BodyPreviewMaxEdge
                                ? BodyPreviewMaxEdge / (float)longestEdge
                                : 1.0f;
                            int previewWidth = Math.Max(1, (int)MathF.Round(frame.Width * previewScale));
                            int previewHeight = Math.Max(1, (int)MathF.Round(frame.Height * previewScale));

                            if (skeletonOnly)
                            {
                                // Inference still consumes the raw camera frame above.
                                // This branch changes only what WinForms displays and
                                // avoids the camera resize/copy entirely.
                                bmp = new Bitmap(previewWidth, previewHeight, PixelFormat.Format24bppRgb);
                                using (var black = Graphics.FromImage(bmp))
                                    black.Clear(Color.Black);
                            }
                            else
                            {
                                // Drawing a 1080p/4K camera image into a much smaller
                                // WinForms box wastes CPU and memory bandwidth. Scale
                                // once before bitmap conversion; normalized landmarks
                                // remain correct at any preview resolution.
                                OpenCvSharp.Mat previewFrame = frame;
                                OpenCvSharp.Mat? scaledPreview = null;
                                if (previewScale < 1.0f)
                                {
                                    scaledPreview = new OpenCvSharp.Mat();
                                    OpenCvSharp.Cv2.Resize(frame, scaledPreview,
                                        new OpenCvSharp.Size(previewWidth, previewHeight),
                                        0, 0, OpenCvSharp.InterpolationFlags.Area);
                                    previewFrame = scaledPreview;
                                }
                                try
                                {
                                    bmp = MatToBitmap(previewFrame);
                                }
                                finally
                                {
                                    scaledPreview?.Dispose();
                                }
                            }
                            bool worldPreviewActive = latestWorldLandmarkFrame != null;
                            if (worldPreviewActive)
                                DrawMediaPipeSkeleton(
                                    bmp,
                                    latestWorldLandmarkFrame!,
                                    _bodyOffX / 100f,
                                    _bodyOffY / 100f);
                            if (_bodyPreviewMirror)
                                bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
                            DrawTrackerSourceBadge(
                                bmp,
                                haveTrackerSelection
                                    ? trackerSelection.Source
                                    : BodyTrackerSource.None,
                                worldPreviewActive);

                            Bitmap queuedBitmap = bmp;
                            BeginInvoke(() =>
                            {
                                try
                                {
                                    if (_bodyPreviewActive
                                        && IsBodyCaptureSessionActive(captureGeneration)
                                        && !IsDisposed
                                        && previewModeVersion == Volatile.Read(ref _bodyPreviewModeVersion))
                                    {
                                        var old = _picBodyVideo.Image;
                                        _picBodyVideo.Image = queuedBitmap;
                                        old?.Dispose();
                                    }
                                    else
                                    {
                                        queuedBitmap.Dispose();
                                    }
                                }
                                finally
                                {
                                    Interlocked.Exchange(ref _bodyPreviewUiPending, 0);
                                }
                            });
                            bmp = null; // ownership moved to the UI callback
                        }
                        catch
                        {
                            bmp?.Dispose();
                            Interlocked.Exchange(ref _bodyPreviewUiPending, 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsBodyCaptureSessionActive(captureGeneration))
                    BodySetStatus("Capture error: " + ex.Message, Color.FromArgb(255, 120, 120));
            }
            finally
            {
                // Clear while this session still owns its socket. The ownership
                // check and send are serialized with new-session registration,
                // so an obsolete delayed finally can never clear a newer feed.
                if (sessionOsc != null)
                    ClearBodyOscSession(sessionOsc);
                try { sessionOsc?.Dispose(); } catch { }
                try { grabberThread?.Join(1000); } catch { }
                try { diag?.Flush(); diag?.Dispose(); } catch { }
                try { calibrationDiag?.Flush(); calibrationDiag?.Dispose(); } catch { }
                try { world3dCalibrationRecorder?.Dispose(); } catch { }
                try { cap?.Release(); cap?.Dispose(); } catch { }
                try { world3dWorker?.Dispose(); } catch { }
                if (Volatile.Read(ref _bodyCaptureGeneration) == captureGeneration)
                {
                    _bodyTrackingStreamReady = false;
                    _bodyPoseValid = false;
                    Volatile.Write(ref _bodyActiveTrackerSource, (int)BodyTrackerSource.None);
                }
            }
        }

        // COCO-WholeBody left/right partners. The last six preserve the foot
        // landmark type while swapping anatomical sides.
        private static readonly int[] KpMirror =
        {
            0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15,
            20, 21, 22, 17, 18, 19
        };

        // Left/right joint pairs for the anti-flip guard
        private static readonly (int L, int R)[] KpPairs =
        {
            (1, 2), (3, 4), (5, 6), (7, 8), (9, 10), (11, 12),
            (13, 14), (15, 16), (17, 20), (18, 21), (19, 22)
        };

#if OCU_LEGACY_RTMP
        // Retained only as non-built migration reference. Production World3D
        // has no ONNX Runtime dependency and cannot execute this code.
        // ImageNet normalization constants (RGB) used by RTMPose-family models
        private static readonly float[] RtmMean = { 123.675f, 116.28f, 103.53f };
        private static readonly float[] RtmStd = { 58.395f, 57.12f, 57.375f };

        // Persistent input/output plumbing for the session. RTMW emits all 133
        // COCO-WholeBody points; legacy MoveNet emits body only (17).
        private sealed class PoseInferenceBuffers : IDisposable
        {
            public readonly int Width;
            public readonly int Height;
            public readonly bool WantsInt;
            public readonly bool Nchw;
            public readonly bool Rtm;
            public readonly byte[] Pixels;
            public readonly float[]? FloatInput;
            public readonly int[]? IntInput;
            public readonly string[] InputNames;
            public readonly OrtValue[] InputValues;
            public readonly string[] OutputNames;
            public readonly RunOptions RunOptions = new();
            public readonly int SimccXIndex;
            public readonly int SimccYIndex;

            public PoseInferenceBuffers(InferenceSession pose, string inputName,
                int width, int height, bool wantsInt, bool nchw, bool rtm)
            {
                Width = width;
                Height = height;
                WantsInt = wantsInt;
                Nchw = nchw;
                Rtm = rtm;
                Pixels = new byte[width * height * 3];
                InputNames = new[] { inputName };
                OutputNames = pose.OutputMetadata.Keys.ToArray();
                SimccXIndex = Array.IndexOf(OutputNames, "simcc_x");
                SimccYIndex = Array.IndexOf(OutputNames, "simcc_y");

                long[] shape = (rtm || nchw)
                    ? new long[] { 1, 3, height, width }
                    : new long[] { 1, height, width, 3 };
                if (wantsInt && !rtm)
                {
                    IntInput = new int[Pixels.Length];
                    InputValues = new[] { OrtValue.CreateTensorValueFromMemory(IntInput, shape) };
                }
                else
                {
                    FloatInput = new float[Pixels.Length];
                    InputValues = new[] { OrtValue.CreateTensorValueFromMemory(FloatInput, shape) };
                }
            }

            public void Dispose()
            {
                foreach (var value in InputValues)
                    value.Dispose();
                RunOptions.Dispose();
            }
        }

        // Reuses pinned input memory and decodes native output spans directly.
        // OpenCV remains BGR; channel conversion is folded into tensor packing.
        private static bool RunPoseOnSquare(InferenceSession pose, OpenCvSharp.Mat bgrSquare,
            PoseInferenceBuffers buffers, float[] destination)
        {
            int w = buffers.Width, h = buffers.Height, hw = w * h;
            Marshal.Copy(bgrSquare.Data, buffers.Pixels, 0, buffers.Pixels.Length);

            if (buffers.Rtm)
            {
                Span<float> dst = buffers.FloatInput!;
                for (int c = 0; c < 3; c++)
                {
                    int srcChannel = 2 - c;
                    float mean = RtmMean[c], std = RtmStd[c];
                    for (int i = 0; i < hw; i++)
                        dst[c * hw + i] = (buffers.Pixels[i * 3 + srcChannel] - mean) / std;
                }
            }
            else if (buffers.WantsInt)
            {
                Span<int> dst = buffers.IntInput!;
                if (buffers.Nchw)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int srcChannel = 2 - c;
                        for (int i = 0; i < hw; i++)
                            dst[c * hw + i] = buffers.Pixels[i * 3 + srcChannel];
                    }
                }
                else
                {
                    for (int i = 0; i < hw; i++)
                    {
                        dst[i * 3] = buffers.Pixels[i * 3 + 2];
                        dst[i * 3 + 1] = buffers.Pixels[i * 3 + 1];
                        dst[i * 3 + 2] = buffers.Pixels[i * 3];
                    }
                }
            }
            else
            {
                Span<float> dst = buffers.FloatInput!;
                if (buffers.Nchw)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        int srcChannel = 2 - c;
                        for (int i = 0; i < hw; i++)
                            dst[c * hw + i] = buffers.Pixels[i * 3 + srcChannel];
                    }
                }
                else
                {
                    for (int i = 0; i < hw; i++)
                    {
                        dst[i * 3] = buffers.Pixels[i * 3 + 2];
                        dst[i * 3 + 1] = buffers.Pixels[i * 3 + 1];
                        dst[i * 3 + 2] = buffers.Pixels[i * 3];
                    }
                }
            }

            using var results = pose.Run(buffers.RunOptions,
                buffers.InputNames, buffers.InputValues, buffers.OutputNames);
            if (!buffers.Rtm)
            {
                var output = results.First().GetTensorDataAsSpan<float>();
                int count = Math.Min(CocoBodyKeypointCount * 3, output.Length);
                if (count < CocoBodyKeypointCount * 3)
                    return false;
                output[..count].CopyTo(destination);
                return true;
            }

            if (buffers.SimccXIndex < 0 || buffers.SimccYIndex < 0)
                return false;
            var sxT = results.ElementAt(buffers.SimccXIndex).GetTensorDataAsSpan<float>();
            var syT = results.ElementAt(buffers.SimccYIndex).GetTensorDataAsSpan<float>();
            int binsX = sxT.Length / TrackedKeypointCount;
            int binsY = syT.Length / TrackedKeypointCount;
            if (binsX <= 0 || binsY <= 0)
                return false;

            for (int k = 0; k < TrackedKeypointCount; k++)
            {
                int axi = 0, ayi = 0;
                float axv = float.MinValue, ayv = float.MinValue;
                for (int b = 0; b < binsX; b++)
                {
                    float v = sxT[k * binsX + b];
                    if (v > axv) { axv = v; axi = b; }
                }
                for (int b = 0; b < binsY; b++)
                {
                    float v = syT[k * binsY + b];
                    if (v > ayv) { ayv = v; ayi = b; }
                }
                destination[k * 3] = (ayi / 2f) / h;
                destination[k * 3 + 1] = (axi / 2f) / w;
                destination[k * 3 + 2] = Math.Clamp(Math.Min(axv, ayv), 0f, 1f);
            }
            return true;
        }
#endif

        // Normalized shoulders-to-ankles span from the previous frame; scales
        // the human-speed ceiling to how large the person is in frame.
        private static float EstimateSpan(float[,] kp, float fallback)
        {
            if (kp[KpLSho, 2] > 0.3f && kp[KpRSho, 2] > 0.3f
                && (kp[KpLAnk, 2] > 0.3f || kp[KpRAnk, 2] > 0.3f))
            {
                float shoY = (kp[KpLSho, 1] + kp[KpRSho, 1]) / 2f;
                float ankY = kp[KpLAnk, 2] > kp[KpRAnk, 2] ? kp[KpLAnk, 1] : kp[KpRAnk, 1];
                float s = ankY - shoY;
                if (s > 0.1f)
                    return s;
            }
            return fallback;
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

        // ── GPU adapter enumeration (DXGI) ────────────────────────────────
        // Lists hardware adapters in the same order DirectML indexes them, so
        // "GPU 1" in the dropdown is the same device AppendExecutionProvider_DML(1)
        // opens. Software adapters (Basic Render Driver) are skipped.

        [DllImport("dxgi.dll")]
        private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

        [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIFactory1
        {
            void _0(); void _1(); void _2(); void _3(); // IDXGIObject
            void _4(); void _5(); void _6(); void _7(); void _8(); // IDXGIFactory
            [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);
        }

        [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDXGIAdapter1
        {
            void _0(); void _1(); void _2(); void _3(); // IDXGIObject
            void _4(); void _5(); void _6(); // IDXGIAdapter
            [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DXGI_ADAPTER_DESC1
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
            public uint VendorId, DeviceId, SubSysId, Revision;
            public IntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
            public long AdapterLuid;
            public uint Flags; // 2 = DXGI_ADAPTER_FLAG_SOFTWARE
        }

        private static List<string> EnumGpuAdapters()
        {
            var list = new List<string>();
            try
            {
                Guid iid = typeof(IDXGIFactory1).GUID;
                if (CreateDXGIFactory1(ref iid, out IntPtr pf) != 0 || pf == IntPtr.Zero)
                    return list;
                var factory = (IDXGIFactory1)Marshal.GetObjectForIUnknown(pf);
                Marshal.Release(pf);
                for (uint i = 0; i < 8; i++)
                {
                    if (factory.EnumAdapters1(i, out var adapter) != 0)
                        break;
                    if (adapter.GetDesc1(out var desc) == 0 && (desc.Flags & 2) == 0)
                        list.Add(desc.Description.Trim());
                    Marshal.ReleaseComObject(adapter);
                }
                Marshal.ReleaseComObject(factory);
            }
            catch { }
            return list;
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
            // Face keypoints (0-4) are never drawn: under a VR headset the
            // model guesses them, and facing the camera straight on it flips
            // the ears/eyes side to side. The head marker is synthesized from
            // the shoulders instead, which carry a real left/right identity.
            for (int i = 5; i < CocoBodyKeypointCount; i++)
            {
                if (kp[i, 2] < minConf) continue;
                float x = kp[i, 0] * bmp.Width, yy = kp[i, 1] * bmp.Height;
                g.FillEllipse(dotBrush, x - 4, yy - 4, 8, 8);
            }
            // Feet use the same yellow joints and green bones as the body.
            for (int i = CocoBodyKeypointCount; i < BodyFootKeypointCount; i++)
            {
                if (kp[i, 2] < minConf) continue;
                float x = kp[i, 0] * bmp.Width, yy = kp[i, 1] * bmp.Height;
                g.FillEllipse(dotBrush, x - 4, yy - 4, 8, 8);
            }

            void DrawHand(int start)
            {
                const float handMinConf = 0.20f;
                foreach (var (a, b) in HandBones)
                {
                    int ia = start + a, ib = start + b;
                    if (kp[ia, 2] < handMinConf || kp[ib, 2] < handMinConf) continue;
                    g.DrawLine(bonePen,
                        kp[ia, 0] * bmp.Width, kp[ia, 1] * bmp.Height,
                        kp[ib, 0] * bmp.Width, kp[ib, 1] * bmp.Height);
                }
                for (int i = 0; i < HandKeypointCount; i++)
                {
                    int k = start + i;
                    if (kp[k, 2] < handMinConf) continue;
                    float x = kp[k, 0] * bmp.Width, yy = kp[k, 1] * bmp.Height;
                    g.FillEllipse(dotBrush, x - 2, yy - 2, 4, 4);
                }
            }
            DrawHand(LeftHandStart);
            DrawHand(RightHandStart);
            if (kp[KpLSho, 2] >= minConf && kp[KpRSho, 2] >= minConf)
            {
                float lx = kp[KpLSho, 0] * bmp.Width, ly = kp[KpLSho, 1] * bmp.Height;
                float rx = kp[KpRSho, 0] * bmp.Width, ry = kp[KpRSho, 1] * bmp.Height;
                float mx = (lx + rx) / 2f, my = (ly + ry) / 2f;
                float span = Math.Max(20f, (float)Math.Sqrt((lx - rx) * (lx - rx) + (ly - ry) * (ly - ry)));
                // Neck at ~half the first-guess length (user-fitted on camera:
                // 0.75 floated the circle well above his actual head).
                float radius = 0.32f * span;
                float cy = my - 0.55f * span;
                using var headPen = new Pen(Color.FromArgb(230, 250, 200, 60), 3f);
                g.DrawEllipse(headPen, mx - radius, cy - radius, radius * 2, radius * 2);
                g.DrawLine(bonePen, mx, my, mx, cy + radius); // neck
            }
        }

        // ── OSC sender ────────────────────────────────────────────────────
        // VRChat OSC tracker format, matching the OCU DLL's receiver:
        // /tracking/trackers/{1..8}/position + /tracking/trackers/head/position
        // Unity convention (x right, y up, +z forward, meters). The pose model
        // is 2D, so forward leg depth is conservatively inferred from apparent
        // thigh/shin shortening against their planted standing projections.

        private static void DrawMediaPipeSkeleton(
            Bitmap bmp,
            WorldLandmarkFrame frame,
            float previewOffsetX,
            float previewOffsetY)
        {
            const float minConfidence = 0.25f;
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var bonePen = new Pen(Color.FromArgb(235, 70, 200, 255), 3f);
            using var jointBrush = new SolidBrush(Color.FromArgb(255, 120, 225, 255));

            bool TryPoint(MediaPipe33LandmarkIndex index, out PointF point)
            {
                WorldLandmark landmark = frame[(int)index];
                Vector3 p = landmark.NormalizedPosition;
                if (!landmark.IsValid
                    || landmark.Confidence < minConfidence
                    || !WorldLandmark.IsFinite(p))
                {
                    point = default;
                    return false;
                }
                point = new PointF(
                    (p.X + previewOffsetX) * bmp.Width,
                    (p.Y + previewOffsetY) * bmp.Height);
                return true;
            }

            foreach (var (a, b) in MediaPipePoseBones)
            {
                if (TryPoint(a, out PointF pa) && TryPoint(b, out PointF pb))
                    g.DrawLine(bonePen, pa, pb);
            }

            for (int i = (int)MediaPipe33LandmarkIndex.LeftShoulder;
                 i <= (int)MediaPipe33LandmarkIndex.RightFootIndex;
                 i++)
            {
                if (!TryPoint((MediaPipe33LandmarkIndex)i, out PointF p))
                    continue;
                float radius = i >= (int)MediaPipe33LandmarkIndex.LeftAnkle ? 3.5f : 4f;
                g.FillEllipse(jointBrush,
                    p.X - radius, p.Y - radius, radius * 2f, radius * 2f);
            }
        }

        private void UpdateMediaPipeSilhouetteState(
            WorldLandmarkFrame? landmarks,
            bool continuousSelected)
        {
            lock (_bodyKpLock)
            {
                Array.Clear(_bodyKp);
                _bodyPoseValid = continuousSelected && landmarks != null;
                if (!_bodyPoseValid)
                    return;

                void Copy(MediaPipe33LandmarkIndex source, params int[] targets)
                {
                    WorldLandmark point = landmarks![(int)source];
                    if (!point.IsValid)
                        return;
                    foreach (int target in targets)
                    {
                        _bodyKp[target, 0] = point.NormalizedPosition.X;
                        _bodyKp[target, 1] = point.NormalizedPosition.Y;
                        _bodyKp[target, 2] = point.Confidence;
                    }
                }

                Copy(MediaPipe33LandmarkIndex.Nose, KpNose);
                Copy(MediaPipe33LandmarkIndex.LeftShoulder, KpLSho);
                Copy(MediaPipe33LandmarkIndex.RightShoulder, KpRSho);
                Copy(MediaPipe33LandmarkIndex.LeftElbow, KpLElb);
                Copy(MediaPipe33LandmarkIndex.RightElbow, KpRElb);
                Copy(MediaPipe33LandmarkIndex.LeftWrist, KpLWri);
                Copy(MediaPipe33LandmarkIndex.RightWrist, KpRWri);
                Copy(MediaPipe33LandmarkIndex.LeftHip, KpLHip);
                Copy(MediaPipe33LandmarkIndex.RightHip, KpRHip);
                Copy(MediaPipe33LandmarkIndex.LeftKnee, KpLKnee);
                Copy(MediaPipe33LandmarkIndex.RightKnee, KpRKnee);
                Copy(MediaPipe33LandmarkIndex.LeftAnkle, KpLAnk);
                Copy(MediaPipe33LandmarkIndex.RightAnkle, KpRAnk);
                Copy(MediaPipe33LandmarkIndex.LeftHeel, KpLHeel);
                Copy(MediaPipe33LandmarkIndex.RightHeel, KpRHeel);
                // MediaPipe has one forefoot point per side; mirror it into the
                // two old display-only toe slots used by the silhouette panel.
                Copy(MediaPipe33LandmarkIndex.LeftFootIndex, KpLBigToe, KpLSmallToe);
                Copy(MediaPipe33LandmarkIndex.RightFootIndex, KpRBigToe, KpRSmallToe);
            }
        }

        private static void WriteWorld3dDiagnosticRow(
            StreamWriter writer,
            long harvestNowMs,
            bool active,
            WorldLandmarkFrame? landmarks,
            BodyTrackerFrame? converted,
            CameraLowerBodyClassification classification)
        {
            var line = new System.Text.StringBuilder(640);
            void AddText(string value)
            {
                if (line.Length != 0) line.Append(';');
                line.Append(value);
            }
            void AddLong(long value) => AddText(value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            void AddUInt(uint value) => AddText(value.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            void AddFloat(float value) => AddText(float.IsFinite(value)
                ? value.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty);
            void AddBool(bool value) => AddText(value ? "1" : "0");
            void AddPose(BodyTrackerPose pose)
            {
                if (pose.PositionValid && WorldLandmark.IsFinite(pose.Position))
                {
                    AddFloat(pose.Position.X); AddFloat(pose.Position.Y); AddFloat(pose.Position.Z);
                }
                else
                {
                    AddText(string.Empty); AddText(string.Empty); AddText(string.Empty);
                }
            }
            void AddLandmark(MediaPipe33LandmarkIndex index)
            {
                if (landmarks == null)
                {
                    AddText(string.Empty); AddText(string.Empty); AddText(string.Empty); AddFloat(0f);
                    return;
                }
                WorldLandmark point = landmarks[(int)index];
                if (point.IsValid && WorldLandmark.IsFinite(point.WorldPosition))
                {
                    AddFloat(point.WorldPosition.X);
                    AddFloat(point.WorldPosition.Y);
                    AddFloat(point.WorldPosition.Z);
                }
                else
                {
                    AddText(string.Empty); AddText(string.Empty); AddText(string.Empty);
                }
                AddFloat(point.Confidence);
            }

            AddLong(harvestNowMs);
            AddLong(landmarks?.Sequence ?? -1L);
            AddLong(landmarks?.TimestampMs ?? -1L);
            AddLong(landmarks != null ? harvestNowMs - landmarks.TimestampMs : -1L);
            AddUInt(landmarks?.SourceEpoch ?? 0u);
            AddBool(active);
            AddBool(converted?.CorePoseValid == true);
            AddText(((int)classification.Left.TransportState).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AddFloat(classification.Left.Confidence);
            AddText(((int)classification.Right.TransportState).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            AddFloat(classification.Right.Confidence);
            AddPose(converted?.GetTracker(1) ?? default);
            AddPose(converted?.GetTracker(2) ?? default);
            AddPose(converted?.GetTracker(3) ?? default);
            AddLandmark(MediaPipe33LandmarkIndex.LeftHip);
            AddLandmark(MediaPipe33LandmarkIndex.LeftKnee);
            AddLandmark(MediaPipe33LandmarkIndex.LeftAnkle);
            AddLandmark(MediaPipe33LandmarkIndex.RightHip);
            AddLandmark(MediaPipe33LandmarkIndex.RightKnee);
            AddLandmark(MediaPipe33LandmarkIndex.RightAnkle);
            writer.WriteLine(line.ToString());
        }

        private static void DrawTrackerSourceBadge(
            Bitmap bmp,
            BodyTrackerSource activeSource,
            bool worldPreviewActive)
        {
            string active = activeSource switch
            {
                BodyTrackerSource.Continuous3D => "WORLD 3D",
                _ => "WAITING",
            };
            string headline = $"MEDIAPIPE WORLD 3D  |  ACTIVE: {active}";
            string detail = worldPreviewActive
                ? "Preview joints: MediaPipe 33"
                : "Waiting for MediaPipe pose";

            Color accent = activeSource switch
            {
                BodyTrackerSource.Continuous3D => Color.FromArgb(90, 215, 255),
                _ => Color.FromArgb(185, 175, 195),
            };

            using var g = Graphics.FromImage(bmp);
            using var headlineFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            using var detailFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            SizeF headlineSize = g.MeasureString(headline, headlineFont);
            SizeF detailSize = g.MeasureString(detail, detailFont);
            float width = Math.Max(headlineSize.Width, detailSize.Width) + 16f;
            float height = headlineSize.Height + detailSize.Height + 10f;
            using var background = new SolidBrush(Color.FromArgb(205, 12, 14, 18));
            using var accentBrush = new SolidBrush(accent);
            using var detailBrush = new SolidBrush(Color.FromArgb(225, 225, 230, 235));
            g.FillRectangle(background, 8f, 8f, width, height);
            g.FillRectangle(accentBrush, 8f, 8f, 4f, height);
            g.DrawString(headline, headlineFont, accentBrush, 16f, 11f);
            g.DrawString(detail, detailFont, detailBrush,
                16f, 11f + headlineSize.Height);
        }

        private void SendBodyOsc(float[,] kp, int imageWidth, int imageHeight, bool sendPackets,
            bool publishLegacyTrackers = true, uint sourceEpoch = 1,
            bool publishGaitArms = true,
            bool publishGaitLegSemantics = true)
        {
            const float minConf = BodyJointMinConfidence;
            const float minLegConf = LegJointMinConfidence;
            const uint legacyFrameFlags = 0x01u | 0x02u | 0x04u | 0x08u | 0x10u;
            uint legacyPoseMask = 0;
            if (sendPackets && _bodyOsc == null) return;

            // Face keypoints are worthless under a VR headset, so scale and
            // head anchor come from SHOULDERS + ANKLES only: shoulder-to-ankle
            // span is ~77% of standing height, and the HMD sits ~16% of that
            // span above the shoulder midpoint.
            float floorCandidate = float.NegativeInfinity;
            int floorPointCount = 0;
            void ConsiderFloor(int i)
            {
                if (kp[i, 2] < minLegConf) return;
                floorCandidate = Math.Max(floorCandidate, kp[i, 1]);
                floorPointCount++;
            }
            // The emitted tracker position and lower-body classifier are both
            // ankle-anchored, so the floor reference must use that same stable
            // source. RTMW heel/toe detail can stay confidently pinned or jump
            // to the other leg during occlusion; letting one such point own the
            // global floor creates an apparent left/right tracking imbalance.
            // Detail points are only a last-resort floor source when neither
            // main ankle is available.
            ConsiderFloor(KpLAnk); ConsiderFloor(KpRAnk);
            if (floorPointCount == 0)
            {
                ConsiderFloor(KpLHeel); ConsiderFloor(KpRHeel);
                ConsiderFloor(KpLBigToe); ConsiderFloor(KpRBigToe);
                ConsiderFloor(KpLSmallToe); ConsiderFloor(KpRSmallToe);
            }
            if (kp[KpLSho, 2] < minConf || kp[KpRSho, 2] < minConf) return;
            // A short foot occlusion must not stop waist/foot packets or reset
            // the coordinate frame. Once calibrated, keep the stable floor and
            // let the per-joint filter bridge the missing measurement.
            if (floorPointCount == 0 && !_bodyFloorValid) return;

            // Image Y grows downward. The lower of the two visible ankles is
            // normally the planted foot, so MAX is the floor candidate; using
            // their average made a lifted foot drag the floor upward with it.
            // Follow newly observed lower ground promptly, but follow upward
            // only glacially so a kick/jump cannot redefine the floor.
            if (!_bodyFloorValid)
            {
                _bodyFloorY = floorCandidate;
                _bodyFloorValid = true;
            }
            else if (floorPointCount > 0)
            {
                float floorDelta = Math.Clamp(floorCandidate - _bodyFloorY, -0.04f, 0.04f);
                float floorAlpha = floorDelta >= 0f ? 0.12f : 0.0015f;
                _bodyFloorY += floorAlpha * floorDelta;
            }

            float shoX = (kp[KpLSho, 0] + kp[KpRSho, 0]) / 2f;
            float shoY = (kp[KpLSho, 1] + kp[KpRSho, 1]) / 2f;
            float observedSpan = _bodyFloorY - shoY;
            if (observedSpan < 0.15f) return;

            float configuredHeightM = BodyReferenceHeightM;
            float targetMetersPerUnit = configuredHeightM * 0.77f / observedSpan;
            if (!float.IsFinite(targetMetersPerUnit) || targetMetersPerUnit < 0.25f || targetMetersPerUnit > 10f)
                return;
            if (!_bodyScaleValid)
            {
                _bodyMetersPerUnit = targetMetersPerUnit;
                _bodyScaleValid = true;
            }
            else
            {
                // Body scale is calibration, not motion. Limit each target
                // correction and use a slow EMA so a one-frame crop/pose error
                // cannot turn a few-centimetre step into a half-metre teleport.
                targetMetersPerUnit = Math.Clamp(targetMetersPerUnit,
                    _bodyMetersPerUnit * 0.75f, _bodyMetersPerUnit * 1.25f);
                _bodyMetersPerUnit += 0.005f * (targetMetersPerUnit - _bodyMetersPerUnit);
            }
            float mPerU = _bodyMetersPerUnit;
            float span = configuredHeightM * 0.77f / mPerU;
            float imageAspect = Legacy2DTrackerGeometry.ResolveImageAspect(
                imageWidth, imageHeight);

            void Send(string addr, float nx, float ny, float z = 0f, float liftGain = 1f)
            {
                // The setup camera faces the player, so image-right is the
                // player's anatomical left. Convert it to body-local +X right
                // before the DLL rotates this body frame into HMD space.
                float ux = Legacy2DTrackerGeometry.MetricX(nx, mPerU, imageAspect);
                float uy = (_bodyFloorY - ny) * mPerU;
                if (uy > 0f && liftGain > 1f)
                    uy *= liftGain;
                if (sendPackets && publishLegacyTrackers)
                    SendOscVec(addr, ux, uy, z);
            }

            bool Has(int i) => kp[i, 2] >= minConf;
            bool HasLeg(int i) => kp[i, 2] >= minLegConf;
            float MetricX(float nx) =>
                Legacy2DTrackerGeometry.MetricX(nx, mPerU, imageAspect);
            float IsotropicImageX(float nx) =>
                Legacy2DTrackerGeometry.ToIsotropicImageX(nx, imageAspect);
            float MetricY(float ny) => (_bodyFloorY - ny) * mPerU;
            const float RadToDeg = 180f / MathF.PI;

            float WrapDegrees(float a)
            {
                while (a > 180f) a -= 360f;
                while (a < -180f) a += 360f;
                return a;
            }

            (float x, float y, float z) SmoothEuler(int tracker, float x, float y, float z, float alpha)
            {
                x = WrapDegrees(x); y = WrapDegrees(y); z = WrapDegrees(z);
                if (!_bodyTrackerEulerValid[tracker])
                {
                    _bodyTrackerEuler[tracker, 0] = x;
                    _bodyTrackerEuler[tracker, 1] = y;
                    _bodyTrackerEuler[tracker, 2] = z;
                    _bodyTrackerEulerValid[tracker] = true;
                }
                else
                {
                    _bodyTrackerEuler[tracker, 0] = WrapDegrees(_bodyTrackerEuler[tracker, 0]
                        + alpha * WrapDegrees(x - _bodyTrackerEuler[tracker, 0]));
                    _bodyTrackerEuler[tracker, 1] = WrapDegrees(_bodyTrackerEuler[tracker, 1]
                        + alpha * WrapDegrees(y - _bodyTrackerEuler[tracker, 1]));
                    _bodyTrackerEuler[tracker, 2] = WrapDegrees(_bodyTrackerEuler[tracker, 2]
                        + alpha * WrapDegrees(z - _bodyTrackerEuler[tracker, 2]));
                }
                return (_bodyTrackerEuler[tracker, 0], _bodyTrackerEuler[tracker, 1], _bodyTrackerEuler[tracker, 2]);
            }

            (float pitch, float yaw, float roll) WaistEuler()
            {
                if (!Has(KpLHip) || !Has(KpRHip)) return (0f, 0f, 0f);
                float dx = MetricX(kp[KpRHip, 0]) - MetricX(kp[KpLHip, 0]);
                float dy = MetricY(kp[KpRHip, 1]) - MetricY(kp[KpLHip, 1]);
                // Always orient the hip line toward local +X. This makes roll
                // independent of whether the camera driver mirrors its image.
                if (dx < 0f) { dx = -dx; dy = -dy; }
                float roll = Math.Clamp(MathF.Atan2(dy, Math.Max(0.001f, dx)) * RadToDeg, -35f, 35f);
                return (0f, 0f, roll);
            }

            (bool valid, float nx, float ny, float pitch, float yaw, float roll, bool detailed) FootPose(int side)
            {
                int ankle = side == 0 ? KpLAnk : KpRAnk;
                int heel = side == 0 ? KpLHeel : KpRHeel;
                int bigToe = side == 0 ? KpLBigToe : KpRBigToe;
                int smallToe = side == 0 ? KpLSmallToe : KpRSmallToe;
                bool hasAnkle = HasLeg(ankle);
                bool hasHeel = HasLeg(heel);
                bool hasBigToe = HasLeg(bigToe);
                bool hasSmallToe = HasLeg(smallToe);
                bool hasToe = hasBigToe || hasSmallToe;
                if (!hasAnkle && !hasHeel && !hasToe)
                    return (false, 0f, 0f, 0f, 0f, 0f, false);

                float toeX = 0f, toeY = 0f;
                if (hasBigToe && hasSmallToe)
                {
                    toeX = (kp[bigToe, 0] + kp[smallToe, 0]) * 0.5f;
                    toeY = (kp[bigToe, 1] + kp[smallToe, 1]) * 0.5f;
                }
                else if (hasToe)
                {
                    int toe = hasBigToe ? bigToe : smallToe;
                    toeX = kp[toe, 0]; toeY = kp[toe, 1];
                }

                // Position follows the COCO ankle, which is the lower-body
                // landmark used by the temporal classifier and depth solver.
                // RTMW's small heel/toe head is valuable for foot rotation, but
                // can remain confidently pinned to the floor while a front kick
                // moves the ankle by half a metre. Blending those stale detail
                // points into position made Skyrim receive a frozen foot while
                // its knee moved. Fall back to the detail points only when the
                // main ankle itself is unavailable.
                float nx, ny;
                if (hasAnkle)
                {
                    nx = kp[ankle, 0];
                    ny = kp[ankle, 1];
                }
                else if (hasHeel && hasToe)
                {
                    nx = 0.45f * kp[heel, 0] + 0.55f * toeX;
                    ny = 0.45f * kp[heel, 1] + 0.55f * toeY;
                }
                else if (hasHeel)
                {
                    nx = kp[heel, 0]; ny = kp[heel, 1];
                }
                else
                {
                    nx = toeX; ny = toeY;
                }

                if (!hasHeel || !hasToe)
                    return (true, nx, ny, 0f, 0f, 0f, false);

                float footLength = Math.Clamp(configuredHeightM * 0.145f, 0.20f, 0.32f);
                if (hasAnkle)
                {
                    float ax = MetricX(kp[ankle, 0]);
                    float ay = MetricY(kp[ankle, 1]);
                    float hx = MetricX(kp[heel, 0]);
                    float hy = MetricY(kp[heel, 1]);
                    float tx = MetricX(toeX);
                    float ty = MetricY(toeY);
                    float heelDistance = MathF.Sqrt((hx - ax) * (hx - ax) + (hy - ay) * (hy - ay));
                    float toeDistance = MathF.Sqrt((tx - ax) * (tx - ax) + (ty - ay) * (ty - ay));
                    float detailLength = MathF.Sqrt((tx - hx) * (tx - hx) + (ty - hy) * (ty - hy));
                    bool coherentDetail = heelDistance <= Math.Max(0.16f, footLength * 0.85f)
                        && toeDistance <= Math.Max(0.24f, footLength * 1.35f)
                        && detailLength >= 0.025f
                        && detailLength <= footLength * 1.35f;
                    if (!coherentDetail)
                        return (true, nx, ny, 0f, 0f, 0f, false);
                }

                float fx = MetricX(toeX) - MetricX(kp[heel, 0]);
                float fy = MetricY(toeY) - MetricY(kp[heel, 1]);
                float projected2 = Math.Min(footLength * footLength - 0.0016f, fx * fx + fy * fy);
                float fz = MathF.Sqrt(Math.Max(0.0016f, footLength * footLength - projected2));
                float pitch = Math.Clamp(-MathF.Atan2(fy, MathF.Sqrt(fx * fx + fz * fz)) * RadToDeg, -50f, 50f);
                float yaw = Math.Clamp(MathF.Atan2(fx, fz) * RadToDeg, -65f, 65f);

                float roll = 0f;
                if (hasBigToe && hasSmallToe)
                {
                    float sx = MetricX(kp[smallToe, 0]) - MetricX(kp[bigToe, 0]);
                    float sy = MetricY(kp[smallToe, 1]) - MetricY(kp[bigToe, 1]);
                    if (sx < 0f) { sx = -sx; sy = -sy; }
                    roll = Math.Clamp(MathF.Atan2(sy, Math.Max(0.001f, sx)) * RadToDeg, -35f, 35f);
                }
                return (true, nx, ny, pitch, yaw, roll, true);
            }

            bool TryPalmPoint(int side, out float nx, out float ny)
            {
                int start = side == 0 ? LeftHandStart : RightHandStart;
                float sumX = 0f, sumY = 0f, sumW = 0f;
                int visible = 0;
                foreach (int offset in PalmPointOffsets)
                {
                    int k = start + offset;
                    float confidence = kp[k, 2];
                    if (confidence < minConf) continue;
                    sumX += kp[k, 0] * confidence;
                    sumY += kp[k, 1] * confidence;
                    sumW += confidence;
                    visible++;
                }
                if (visible < 3 || sumW <= 0f)
                {
                    nx = ny = 0f;
                    return false;
                }
                nx = sumX / sumW;
                ny = sumY / sumW;
                return true;
            }

            (bool valid, float feature, float handY) ArmFeature(int side)
            {
                int shoulder = side == 0 ? KpLSho : KpRSho;
                int elbow = side == 0 ? KpLElb : KpRElb;
                int wrist = side == 0 ? KpLWri : KpRWri;
                if (!Has(shoulder) || !Has(elbow))
                    return (false, 0f, 0f);

                bool hasPalm = TryPalmPoint(side, out float handNx, out float handNy);
                if (!hasPalm && !Has(wrist))
                    return (false, 0f, 0f);
                if (!hasPalm)
                {
                    handNx = kp[wrist, 0];
                    handNy = kp[wrist, 1];
                }

                float sx = MetricX(kp[shoulder, 0]), sy = MetricY(kp[shoulder, 1]);
                float ex = MetricX(kp[elbow, 0]), ey = MetricY(kp[elbow, 1]);
                float wx = MetricX(handNx), wy = MetricY(handNy);
                float Dist(float ax, float ay, float bx, float by)
                {
                    float dx = ax - bx, dy = ay - by;
                    return MathF.Sqrt(dx * dx + dy * dy);
                }

                float upper = Dist(sx, sy, ex, ey);
                float lower = Dist(ex, ey, wx, wy);
                float directReach = Dist(sx, sy, wx, wy);
                float bend = Math.Max(0f, upper + lower - directReach);
                // Inward is anatomical: toward the torso is + for either arm.
                float inward = side == 0 ? wx - sx : sx - wx;
                float lift = wy - sy;

                // A front camera cannot measure forward depth directly, but a
                // real arm pump changes projected reach, elbow bend, inward
                // travel and wrist height together. Their left-right difference
                // is a stable signed phase signal and common body sway cancels.
                float feature = 0.45f * directReach + 0.30f * inward
                    + 0.20f * lift - 0.15f * bend;
                return (true, feature, wy);
            }

            long outputNowMs = Environment.TickCount64;
            CameraLegObservation ObserveLeg(int side)
            {
                int hip = side == 0 ? KpLHip : KpRHip;
                int knee = side == 0 ? KpLKnee : KpRKnee;
                int ankle = side == 0 ? KpLAnk : KpRAnk;
                var output = new Vector3(
                    _bodyOutputFootPosition[side, 0],
                    _bodyOutputFootPosition[side, 1],
                    _bodyOutputFootPosition[side, 2]);
                return new CameraLegObservation(
                    new CameraJoint2D(IsotropicImageX(kp[hip, 0]), kp[hip, 1], kp[hip, 2]),
                    new CameraJoint2D(IsotropicImageX(kp[knee, 0]), kp[knee, 1], kp[knee, 2]),
                    new CameraJoint2D(IsotropicImageX(kp[ankle, 0]), kp[ankle, 1], kp[ankle, 2]),
                    Math.Max(0f, MetricY(kp[ankle, 1])),
                    _bodyFootDepth[side],
                    _bodyOutputFootValid[side],
                    output);
            }

            _bodyLegClassification = _bodyLegClassifier.Update(
                new CameraLowerBodyFrame(outputNowMs, mPerU, ObserveLeg(0), ObserveLeg(1)));

            CameraLegClassification LegMotion(int side) =>
                side == 0 ? _bodyLegClassification.Left : _bodyLegClassification.Right;

            // A yawed person can place the far leg much higher in image space
            // even while both feet are planted on the same real floor. Remove
            // each leg's learned neutral lift before emitting tracker Y. This
            // is the per-leg baseline missing from the old global-floor solver.
            float LegFloorRelativeY(int side, float ny, float liftGain = 1f)
            {
                var motion = LegMotion(side);
                float neutralLift = motion.BaselineReady
                    ? Math.Max(0f, motion.NeutralLiftMeters) : 0f;
                float y = MetricY(ny) - neutralLift;
                if (y > 0f && liftGain > 1f)
                    y *= liftGain;
                return y;
            }

            void SendLeg(string addr, int side, float nx, float ny, float z = 0f, float liftGain = 1f)
            {
                if (!sendPackets || !publishLegacyTrackers) return;
                SendOscVec(addr, MetricX(nx), LegFloorRelativeY(side, ny, liftGain), z);
            }

            // A front camera cannot directly observe depth, but a leg segment
            // that swings toward the camera becomes shorter in the 2D image.
            // Use the longest believable planted projection as its calibrated
            // length. This is deliberately conservative and smoothed: it adds
            // a usable forward kick without pretending monocular vision is a
            // true six-point tracker setup.
            (float kneeDepth, float footDepth) InferLegDepth(int side)
            {
                int hip = side == 0 ? KpLHip : KpRHip;
                int knee = side == 0 ? KpLKnee : KpRKnee;
                int ankle = side == 0 ? KpLAnk : KpRAnk;
                var motion = LegMotion(side);

                // Depth used to use fixed per-inference-frame alphas. The live
                // CPU camera is commonly ~7 FPS while a GPU can exceed 30 FPS,
                // so identical motion had radically different attack/decay.
                // Preserve the existing 30 FPS tuning but normalize it to real
                // elapsed time for both paths.
                long previousDepthMs = _bodyDepthUpdateTimeMs[side];
                float depthDt = previousDepthMs > 0
                    ? Math.Clamp((outputNowMs - previousDepthMs) / 1000f, 1f / 120f, 0.25f)
                    : 1f / 30f;
                _bodyDepthUpdateTimeMs[side] = outputNowMs;
                float DepthAlpha(float alphaAt30Fps) => 1f - MathF.Pow(
                    1f - Math.Clamp(alphaAt30Fps, 0f, 1f), depthDt * 30f);

                void DecayUnreliableDepth()
                {
                    // Brief self-occlusion is normal at kick extension. Preserve
                    // most of an already-confirmed strike through the classifier's
                    // grace window, but never freeze ordinary/knee-raise depth in
                    // place when its landmarks disappear.
                    bool kickGrace = motion.State == CameraLegMotionState.KickExtend
                        || (motion.State == CameraLegMotionState.Recover && motion.BlocksGait);
                    float alpha = DepthAlpha(kickGrace ? 0.025f : 0.12f);
                    _bodyKneeDepth[side] += alpha * (0f - _bodyKneeDepth[side]);
                    _bodyFootDepth[side] += alpha * (0f - _bodyFootDepth[side]);
                    if (_bodyKneeDepth[side] < 0.015f) _bodyKneeDepth[side] = 0f;
                    if (_bodyFootDepth[side] < 0.015f) _bodyFootDepth[side] = 0f;
                }

                if (kp[hip, 2] < minConf || kp[knee, 2] < minLegConf || kp[ankle, 2] < minLegConf)
                {
                    _bodyLegStraightness[side] = -1f;
                    _bodyFootLift[side] = -1f;
                    DecayUnreliableDepth();
                    return (_bodyKneeDepth[side], _bodyFootDepth[side]);
                }

                if (motion.AnatomicalFlipRejected)
                {
                    // Do not advance depth from an impossible ankle-over-knee
                    // frame. State-aware decay keeps a confirmed kick continuous
                    // but drains an ordinary false extension instead of pinning it.
                    _bodyLegStraightness[side] = -1f;
                    _bodyFootLift[side] = -1f;
                    DecayUnreliableDepth();
                    return (_bodyKneeDepth[side], _bodyFootDepth[side]);
                }

                float Dist(int a, int b)
                {
                    float dx = Legacy2DTrackerGeometry.ImageDeltaToMeters(
                        kp[a, 0] - kp[b, 0], mPerU, imageAspect);
                    float dy = (kp[a, 1] - kp[b, 1]) * mPerU;
                    return MathF.Sqrt(dx * dx + dy * dy);
                }

                float thigh = Dist(hip, knee);
                float shin = Dist(knee, ankle);
                float ankleLift = Math.Max(0f, LegFloorRelativeY(side, kp[ankle, 1]));
                float hipAnkle = Dist(hip, ankle);
                float straightness = Math.Clamp(hipAnkle / Math.Max(0.05f, thigh + shin), 0f, 1f);
                _bodyLegStraightness[side] = straightness;
                _bodyFootLift[side] = ankleLift;
                bool planted = motion.BaselineReady && motion.IsValid
                    ? motion.State == CameraLegMotionState.Grounded
                    : ankleLift < 0.10f;
                float nominalThigh = configuredHeightM * 0.245f;
                float nominalShin = configuredHeightM * 0.246f;
                float calibratedThigh = Math.Clamp(thigh, nominalThigh * 0.72f, nominalThigh * 1.12f);
                float calibratedShin = Math.Clamp(shin, nominalShin * 0.72f, nominalShin * 1.12f);

                if (_bodyThighProjection[side] <= 0f && planted)
                    _bodyThighProjection[side] = calibratedThigh;
                if (_bodyShinProjection[side] <= 0f && planted)
                    _bodyShinProjection[side] = calibratedShin;
                // Do not learn segment lengths from a raised/kicking pose.
                // While planted, follow both upward AND downward calibration
                // errors so one long crop/model frame cannot permanently pin
                // inferred depth at its 0.75m maximum.
                if (planted)
                {
                    _bodyThighProjection[side] += 0.10f * (calibratedThigh - _bodyThighProjection[side]);
                    _bodyShinProjection[side] += 0.10f * (calibratedShin - _bodyShinProjection[side]);
                }

                float Depth(float calibrated, float projected)
                {
                    float d2 = calibrated * calibrated - Math.Min(calibrated, projected) * Math.Min(calibrated, projected);
                    float d = MathF.Sqrt(Math.Max(0f, d2));
                    return d < 0.025f ? 0f : d;
                }

                float thighDepth = planted || _bodyThighProjection[side] <= 0f
                    ? 0f : Depth(_bodyThighProjection[side], thigh);
                float shinDepth = planted || _bodyShinProjection[side] <= 0f
                    ? 0f : Depth(_bodyShinProjection[side], shin);
                // A kick has only a few inference frames before the leg starts
                // returning. Preserve nearly all geometrically supported depth
                // and attack it quickly; decay remains slower to avoid a snap at
                // extension. The temporal leg classifier rejects impossible
                // ankle/knee flips before this solve is allowed to advance.
                float targetKnee = Math.Clamp(0.95f * thighDepth, 0f, 0.45f);
                float targetFoot = Math.Clamp(0.95f * (thighDepth + shinDepth), 0f, 0.90f);

                // SkyrimVR-FBT solves the knee from waist + foot trackers; it
                // does not use our camera knee point to decide leg extension.
                // When the camera clearly sees a raised, straight leg, ensure
                // the virtual foot target actually reaches the edge of the
                // calibrated leg sphere. The old depth target remained well
                // inside that sphere, so two-bone IK had no choice but to keep
                // the avatar's knee bent even during a real straight kick.
                // A raw height/straightness threshold cannot distinguish a high
                // march from a strike. Full reach is armed only after the
                // classifier observes a chamber followed by distal extension;
                // ordinary steps and held knees therefore remain bent/animated.
                // KickExtend is only transported for a valid classifier frame.
                // Do not impose a second confidence gate here: ankle confidence
                // naturally falls when a straight leg points into the camera,
                // which previously recognized the kick but withheld full reach.
                bool confirmedKick = motion.IsValid
                    && motion.State == CameraLegMotionState.KickExtend;
                if (confirmedKick)
                {
                    // KickExtend already means the temporal classifier saw a
                    // chamber followed by distal extension. Do not scale the
                    // IK reach back down from the current 2D straightness: a
                    // front-facing straight kick is foreshortened by definition
                    // and can have a very small hip-to-ankle image projection.
                    float legLength = nominalThigh + nominalShin;
                    float desiredReach = legLength * 0.998f;

                    // Solve depth against the position that is actually sent
                    // to SkyrimVR-FBT. The old solve used raw image dx/dy, but
                    // output Y has the per-leg neutral removed and a 1.45 lift
                    // gain. That mismatch made the target get *closer* to the
                    // pelvis during a kick, leaving the two-bone IK visibly
                    // bent. Match the later StabilizeFootX kick clamp as well.
                    bool bothHips = kp[KpLHip, 2] >= minConf && kp[KpRHip, 2] >= minConf;
                    float pelvisNx = bothHips
                        ? (kp[KpLHip, 0] + kp[KpRHip, 0]) * 0.5f : kp[hip, 0];
                    float pelvisNy = bothHips
                        ? (kp[KpLHip, 1] + kp[KpRHip, 1]) * 0.5f : kp[hip, 1];
                    float outputFootNx = kp[ankle, 0];
                    if (_bodyFootNeutralValid[side])
                    {
                        float hipCenterNx = bothHips ? pelvisNx : kp[hip, 0];
                        float neutralNx = hipCenterNx + Legacy2DTrackerGeometry.MetersToImageDelta(
                            _bodyFootNeutralOffsetM[side], mPerU, imageAspect);
                        float kickAllowanceU = Legacy2DTrackerGeometry.MetersToImageDelta(
                            0.42f, mPerU, imageAspect);
                        outputFootNx = neutralNx + Math.Clamp(
                            outputFootNx - neutralNx, -kickAllowanceU, kickAllowanceU);
                    }

                    float dx = MetricX(outputFootNx) - MetricX(pelvisNx);
                    float outputFootY = LegFloorRelativeY(side, kp[ankle, 1], 1.45f);
                    float pelvisY = MetricY(pelvisNy);
                    float dy = outputFootY - pelvisY;
                    float requiredDepth = MathF.Sqrt(Math.Max(0f,
                        desiredReach * desiredReach - dx * dx - dy * dy));
                    targetFoot = Math.Max(targetFoot, Math.Min(0.90f, requiredDepth));
                    targetKnee = Math.Max(targetKnee,
                        Math.Min(0.48f, requiredDepth * nominalThigh / legLength));
                }
                else
                {
                    // Segment foreshortening alone is ambiguous: a bent knee
                    // raise shortens the shin projection just like a kick. Keep
                    // a non-kick foot roughly underneath its inferred knee in
                    // depth. Only the confirmed chamber -> extension state may
                    // add the shin's apparent depth and straighten the leg.
                    targetFoot = Math.Min(targetFoot, targetKnee + 0.065f);
                }

                float kneeAlphaAt30 = targetKnee > _bodyKneeDepth[side]
                    ? confirmedKick ? 0.70f : 0.55f : planted ? 0.45f : 0.16f;
                float footAlphaAt30 = targetFoot > _bodyFootDepth[side]
                    ? confirmedKick ? 0.70f : 0.55f : planted ? 0.45f : 0.16f;
                float kneeAlpha = DepthAlpha(kneeAlphaAt30);
                float footAlpha = DepthAlpha(footAlphaAt30);
                _bodyKneeDepth[side] += kneeAlpha * (targetKnee - _bodyKneeDepth[side]);
                _bodyFootDepth[side] += footAlpha * (targetFoot - _bodyFootDepth[side]);
                Legacy2DTrackerGeometry.PinGroundedDepth(
                    planted,
                    ref _bodyKneeDepth[side],
                    ref _bodyFootDepth[side]);
                return (_bodyKneeDepth[side], _bodyFootDepth[side]);
            }

            var leftDepth = InferLegDepth(0);
            var rightDepth = InferLegDepth(1);
            var leftFoot = FootPose(0);
            var rightFoot = FootPose(1);

            float hipCenterX = (kp[KpLHip, 0] + kp[KpRHip, 0]) * 0.5f;
            float StabilizeFootX(int side, bool valid, float footX, float footY, float footDepth)
            {
                if (!valid) return footX;
                float lift = Math.Max(0f, LegFloorRelativeY(side, footY));
                float offsetM = Legacy2DTrackerGeometry.ImageDeltaToMeters(
                    footX - hipCenterX, mPerU, imageAspect);
                if (lift < 0.08f && footDepth < 0.05f)
                {
                    if (!_bodyFootNeutralValid[side])
                    {
                        _bodyFootNeutralOffsetM[side] = offsetM;
                        _bodyFootNeutralValid[side] = true;
                    }
                    else
                    {
                        _bodyFootNeutralOffsetM[side] += 0.04f * (offsetM - _bodyFootNeutralOffsetM[side]);
                    }
                    return footX;
                }
                if (!_bodyFootNeutralValid[side] || (lift < 0.12f && footDepth < 0.08f))
                    return footX;

                float neutralX = hipCenterX + Legacy2DTrackerGeometry.MetersToImageDelta(
                    _bodyFootNeutralOffsetM[side], mPerU, imageAspect);
                // Permit natural hip-width motion, but prevent a forward kick
                // from turning camera X jitter into the dominant tracker axis.
                // At +/-45 degrees real forward extension projects mostly onto
                // image X. The calibration set showed the old 10 cm clamp
                // crushing exactly one leg in each mirrored view, so confirmed
                // kick/recovery states get enough room for that real projection.
                var motion = LegMotion(side);
                bool kickProjection = motion.State == CameraLegMotionState.KickExtend
                    || (motion.State == CameraLegMotionState.Recover && motion.BlocksGait);
                float lateralAllowanceM = kickProjection
                    ? 0.42f : 0.10f + Math.Min(0.06f, lift * 0.20f);
                float lateralAllowanceU = Legacy2DTrackerGeometry.MetersToImageDelta(
                    lateralAllowanceM, mPerU, imageAspect);
                return neutralX + Math.Clamp(footX - neutralX, -lateralAllowanceU, lateralAllowanceU);
            }

            if (_bodyLegClassification.Left.AnatomicalFlipRejected)
                leftFoot.valid = false;
            if (_bodyLegClassification.Right.AnatomicalFlipRejected)
                rightFoot.valid = false;
            leftFoot.nx = StabilizeFootX(0, leftFoot.valid, leftFoot.nx, leftFoot.ny, leftDepth.footDepth);
            rightFoot.nx = StabilizeFootX(1, rightFoot.valid, rightFoot.nx, rightFoot.ny, rightDepth.footDepth);

            void UpdateOutputFoot(int side, bool valid, float nx, float ny, float z)
            {
                if (!valid)
                {
                    _bodyOutputFootValid[side] = false;
                    _bodyOutputFootTimeMs[side] = 0;
                    Array.Clear(_bodyOutputFootVelocity, side * 3, 3);
                    return;
                }

                float x = MetricX(nx);
                float y = LegFloorRelativeY(side, ny, 1.45f);
                long previousMs = _bodyOutputFootTimeMs[side];
                float dt = previousMs > 0 ? (outputNowMs - previousMs) / 1000f : 0f;
                if (_bodyOutputFootValid[side] && dt >= 0.010f && dt <= 0.250f)
                {
                    _bodyOutputFootVelocity[side, 0] = (x - _bodyOutputFootPosition[side, 0]) / dt;
                    _bodyOutputFootVelocity[side, 1] = (y - _bodyOutputFootPosition[side, 1]) / dt;
                    _bodyOutputFootVelocity[side, 2] = (z - _bodyOutputFootPosition[side, 2]) / dt;
                }
                else
                {
                    _bodyOutputFootVelocity[side, 0] = 0f;
                    _bodyOutputFootVelocity[side, 1] = 0f;
                    _bodyOutputFootVelocity[side, 2] = 0f;
                }
                _bodyOutputFootPosition[side, 0] = x;
                _bodyOutputFootPosition[side, 1] = y;
                _bodyOutputFootPosition[side, 2] = z;
                _bodyOutputFootTimeMs[side] = outputNowMs;
                _bodyOutputFootValid[side] = true;
            }
            UpdateOutputFoot(0, leftFoot.valid, leftFoot.nx, leftFoot.ny, leftDepth.footDepth);
            UpdateOutputFoot(1, rightFoot.valid, rightFoot.nx, rightFoot.ny, rightDepth.footDepth);

            // Head (alignment reference for the DLL): synthesized above the
            // shoulders, never from the (hallucinated) face. Its identity
            // rotation declares the camera's body-forward frame so the DLL
            // can lock that frame to the real HMD yaw at startup.
            Send("/tracking/trackers/head/position", shoX, shoY - 0.16f * span);
            if (sendPackets && publishLegacyTrackers)
                SendOscVec("/tracking/trackers/head/rotation", 0f, 0f, 0f);
            legacyPoseMask |= (1u << 16) | (1u << 17);

            // Hand references use the detected palm center when at least three
            // palm landmarks are visible and fall back to the body wrist when
            // the camera loses the fingers. Walking uses the explicit skeleton
            // phase packet below—not controller positions or fake depth.
            if (TryPalmPoint(0, out float leftHandX, out float leftHandY))
                Send("/tracking/trackers/lhand/position", leftHandX, leftHandY);
            else if (kp[KpLWri, 2] > minConf)
                Send("/tracking/trackers/lhand/position", kp[KpLWri, 0], kp[KpLWri, 1]);
            if (TryPalmPoint(1, out float rightHandX, out float rightHandY))
                Send("/tracking/trackers/rhand/position", rightHandX, rightHandY);
            else if (kp[KpRWri, 2] > minConf)
                Send("/tracking/trackers/rhand/position", kp[KpRWri, 0], kp[KpRWri, 1]);

            var leftArm = ArmFeature(0);
            var rightArm = ArmFeature(1);
            _bodyOutputArmValid = leftArm.valid && rightArm.valid;
            if (leftArm.valid && rightArm.valid)
            {
                float rawPhase = leftArm.feature - rightArm.feature;
                if (!_bodyArmPhaseValid)
                {
                    _bodyArmPhaseCenter = rawPhase;
                    _bodyArmPhaseValid = true;
                }
                else
                {
                    _bodyArmPhaseCenter += 0.004f * (rawPhase - _bodyArmPhaseCenter);
                }
                float phaseMeters = Math.Clamp(3.0f * (rawPhase - _bodyArmPhaseCenter), -0.40f, 0.40f);
                _bodyOutputArmFeature[0] = leftArm.feature;
                _bodyOutputArmFeature[1] = rightArm.feature;
                _bodyOutputArmPhase = phaseMeters;
                _bodyOutputHandY[0] = leftArm.handY;
                _bodyOutputHandY[1] = rightArm.handY;
                // x = signed L-vs-R arm phase; y/z = skeleton hand heights.
                if (sendPackets && publishGaitArms)
                    SendOscVec("/tracking/gait/arms", phaseMeters, leftArm.handY, rightArm.handY);
            }
            else
            {
                _bodyOutputArmPhase = 0f;
            }

            // Semantic camera-leg contract consumed by both gait arbitration
            // and camera-foot kick passthrough in the DLL. State is a stable
            // wire enum; angle and confidence make runtime diagnostics useful.
            if (sendPackets && publishGaitLegSemantics)
            {
                SendOscVec("/tracking/gait/leg/left",
                    (float)_bodyLegClassification.Left.TransportState,
                    _bodyLegClassification.Left.KneeAngleDegrees,
                    _bodyLegClassification.Left.Confidence);
                SendOscVec("/tracking/gait/leg/right",
                    (float)_bodyLegClassification.Right.TransportState,
                    _bodyLegClassification.Right.KneeAngleDegrees,
                    _bodyLegClassification.Right.Confidence);
            }

            // Three complete virtual Vive trackers: waist + both feet. Each
            // gets position AND rotation, matching the physical 6DoF contract
            // SkyrimVR-FBT was written for.
            if (kp[KpLHip, 2] > minConf && kp[KpRHip, 2] > minConf)
            {
                Send("/tracking/trackers/1/position",
                    (kp[KpLHip, 0] + kp[KpRHip, 0]) / 2f, (kp[KpLHip, 1] + kp[KpRHip, 1]) / 2f);
                var waistTarget = WaistEuler();
                var e = SmoothEuler(0, waistTarget.pitch, waistTarget.yaw, waistTarget.roll, 0.16f);
                if (sendPackets && publishLegacyTrackers)
                    SendOscVec("/tracking/trackers/1/rotation", e.x, e.y, e.z);
                legacyPoseMask |= (1u << 0) | (1u << 8);
            }

            (float x, float y, float z) ResolveFootEuler(
                int side,
                bool detailed,
                float detailPitch,
                float detailYaw,
                float detailRoll,
                float footNx,
                float footNy,
                float kneeDepth,
                float footDepth)
            {
                int tracker = side + 1;
                var motion = LegMotion(side);

                if (motion.IsValid && motion.State == CameraLegMotionState.Grounded)
                {
                    // Ground contact is the one state where heel/toe detail is
                    // stable enough to own the foot orientation.
                    return detailed
                        ? SmoothEuler(tracker, detailPitch, detailYaw, detailRoll, 0.24f)
                        : SmoothEuler(tracker, 0f, 0f, 0f, 0.10f);
                }

                bool kickPhase = motion.IsValid
                    && (motion.State == CameraLegMotionState.KickExtend
                        || (motion.State == CameraLegMotionState.Recover && motion.BlocksGait));
                if (kickPhase && !motion.AnatomicalFlipRejected)
                {
                    int knee = side == 0 ? KpLKnee : KpRKnee;
                    if (kp[knee, 2] >= minLegConf)
                    {
                        var kneePosition = new Vector3(
                            MetricX(kp[knee, 0]),
                            LegFloorRelativeY(side, kp[knee, 1]),
                            kneeDepth);
                        var footPosition = new Vector3(
                            MetricX(footNx),
                            LegFloorRelativeY(side, footNy, 1.45f),
                            footDepth);
                        if (CameraFootOrientationSolver.TrySolveKickEuler(
                            kneePosition, footPosition, out Vector3 target))
                        {
                            // Attack quickly enough to follow the extension,
                            // but retain the existing Euler continuity filter.
                            return SmoothEuler(tracker, target.X, target.Y, target.Z, 0.45f);
                        }
                    }
                }

                if (motion.IsValid && motion.State != CameraLegMotionState.Grounded)
                {
                    // A walk step, chamber or knee hold should keep the sole
                    // approximately level and body-forward. Ease there rather
                    // than freezing the exact grounded angle from takeoff.
                    float alpha = motion.State == CameraLegMotionState.Recover ? 0.18f : 0.10f;
                    return SmoothEuler(tracker, 0f, 0f, 0f, alpha);
                }

                // Invalid semantic frames cannot safely steer orientation.
                // Preserve continuity until tracking recovers.
                return _bodyTrackerEulerValid[tracker]
                    ? (_bodyTrackerEuler[tracker, 0], _bodyTrackerEuler[tracker, 1], _bodyTrackerEuler[tracker, 2])
                    : (0f, 0f, 0f);
            }

            if (leftFoot.valid)
            {
                SendLeg("/tracking/trackers/2/position", 0,
                    leftFoot.nx, leftFoot.ny, leftDepth.footDepth, 1.45f);
                var e = ResolveFootEuler(0, leftFoot.detailed,
                    leftFoot.pitch, leftFoot.yaw, leftFoot.roll,
                    leftFoot.nx, leftFoot.ny,
                    leftDepth.kneeDepth, leftDepth.footDepth);
                if (sendPackets && publishLegacyTrackers)
                    SendOscVec("/tracking/trackers/2/rotation", e.x, e.y, e.z);
                legacyPoseMask |= (1u << 1) | (1u << 9);
            }
            if (rightFoot.valid)
            {
                SendLeg("/tracking/trackers/3/position", 1,
                    rightFoot.nx, rightFoot.ny, rightDepth.footDepth, 1.45f);
                var e = ResolveFootEuler(1, rightFoot.detailed,
                    rightFoot.pitch, rightFoot.yaw, rightFoot.roll,
                    rightFoot.nx, rightFoot.ny,
                    rightDepth.kneeDepth, rightDepth.footDepth);
                if (sendPackets && publishLegacyTrackers)
                    SendOscVec("/tracking/trackers/3/rotation", e.x, e.y, e.z);
                legacyPoseMask |= (1u << 2) | (1u << 10);
            }
            if (kp[KpLKnee, 2] >= minLegConf
                && !_bodyLegClassification.Left.AnatomicalFlipRejected)
            {
                SendLeg("/tracking/trackers/4/position", 0,
                    kp[KpLKnee, 0], kp[KpLKnee, 1], leftDepth.kneeDepth);
                legacyPoseMask |= 1u << 3;
            }
            if (kp[KpRKnee, 2] >= minLegConf
                && !_bodyLegClassification.Right.AnatomicalFlipRejected)
            {
                SendLeg("/tracking/trackers/5/position", 1,
                    kp[KpRKnee, 0], kp[KpRKnee, 1], rightDepth.kneeDepth);
                legacyPoseMask |= 1u << 4;
            }
            if (kp[KpLElb, 2] > minConf)
            {
                Send("/tracking/trackers/6/position", kp[KpLElb, 0], kp[KpLElb, 1]);
                legacyPoseMask |= 1u << 5;
            }
            if (kp[KpRElb, 2] > minConf)
            {
                Send("/tracking/trackers/7/position", kp[KpRElb, 0], kp[KpRElb, 1]);
                legacyPoseMask |= 1u << 6;
            }
            if (kp[KpLSho, 2] > minConf && kp[KpRSho, 2] > minConf)
            {
                Send("/tracking/trackers/8/position",
                    (kp[KpLSho, 0] + kp[KpRSho, 0]) / 2f, (kp[KpLSho, 1] + kp[KpRSho, 1]) / 2f); // chest
                legacyPoseMask |= 1u << 7;
            }

            // Packets above are staged by the DLL once it has seen this
            // protocol. Commit them last so a render frame can never observe
            // a waist from one inference and a foot from the next.
            if (sendPackets && publishLegacyTrackers)
                SendOscVec("/tracking/trackers/frame",
                    sourceEpoch, legacyPoseMask, legacyFrameFlags);
        }

        private static BodyTrackerFrame CreateLegacyMuxMarker(
            long sequence,
            long timestampMs,
            uint sourceEpoch)
        {
            var valid = new BodyTrackerPose(
                Vector3.Zero,
                Quaternion.Identity,
                confidence: 1f,
                positionValid: true,
                rotationValid: true);
            var trackers = new BodyTrackerPose[BodyTrackerFrame.TrackerSlotCount];
            trackers[0] = valid;
            trackers[1] = valid;
            trackers[2] = valid;
            return new BodyTrackerFrame(
                sequence,
                timestampMs,
                sourceEpoch,
                BodyTrackerSource.Legacy2D,
                BodyPoseSourceFlags.Legacy2DReconstruction,
                valid,
                trackers);
        }

        private static bool TryBuildContinuousGaitArms(
            WorldLandmarkFrame landmarks,
            BodyTrackerFrame trackerFrame,
            out Vector3 gaitArms)
        {
            const float minimumConfidence = 0.25f;

            bool TryPoint(MediaPipe33LandmarkIndex index, out Vector3 point)
            {
                WorldLandmark landmark = landmarks[(int)index];
                if (!landmark.IsWorldUsable(minimumConfidence))
                {
                    point = Vector3.Zero;
                    return false;
                }
                Vector3 raw = landmark.WorldPosition;
                point = new Vector3(
                    (landmarks.InputMirrored ? -1f : 1f) * raw.X,
                    -raw.Y,
                    -raw.Z);
                return WorldLandmark.IsFinite(point);
            }

            if (!TryPoint(MediaPipe33LandmarkIndex.LeftShoulder, out Vector3 leftShoulder)
                || !TryPoint(MediaPipe33LandmarkIndex.RightShoulder, out Vector3 rightShoulder)
                || !TryPoint(MediaPipe33LandmarkIndex.LeftElbow, out Vector3 leftElbow)
                || !TryPoint(MediaPipe33LandmarkIndex.RightElbow, out Vector3 rightElbow)
                || !TryPoint(MediaPipe33LandmarkIndex.LeftWrist, out Vector3 leftWrist)
                || !TryPoint(MediaPipe33LandmarkIndex.RightWrist, out Vector3 rightWrist))
            {
                gaitArms = Vector3.Zero;
                return false;
            }

            float floorY = float.PositiveInfinity;
            void ConsiderFloor(MediaPipe33LandmarkIndex index)
            {
                if (TryPoint(index, out Vector3 point))
                    floorY = MathF.Min(floorY, point.Y);
            }
            ConsiderFloor(MediaPipe33LandmarkIndex.LeftAnkle);
            ConsiderFloor(MediaPipe33LandmarkIndex.RightAnkle);
            ConsiderFloor(MediaPipe33LandmarkIndex.LeftHeel);
            ConsiderFloor(MediaPipe33LandmarkIndex.RightHeel);
            ConsiderFloor(MediaPipe33LandmarkIndex.LeftFootIndex);
            ConsiderFloor(MediaPipe33LandmarkIndex.RightFootIndex);
            if (!float.IsFinite(floorY))
            {
                gaitArms = Vector3.Zero;
                return false;
            }

            BodyTrackerPose waist = trackerFrame.GetTracker(1);
            Vector3 bodyForward = waist.RotationValid
                ? Vector3.Transform(Vector3.UnitZ, waist.Orientation)
                : Vector3.UnitZ;
            if (!WorldLandmark.IsFinite(bodyForward)
                || bodyForward.LengthSquared() <= 1e-8f)
                bodyForward = Vector3.UnitZ;
            else
                bodyForward = Vector3.Normalize(bodyForward);

            float ArmFeature(Vector3 shoulder, Vector3 elbow, Vector3 wrist) =>
                0.70f * Vector3.Dot(wrist - shoulder, bodyForward)
                + 0.30f * Vector3.Dot(elbow - shoulder, bodyForward);

            float leftFeature = ArmFeature(leftShoulder, leftElbow, leftWrist);
            float rightFeature = ArmFeature(rightShoulder, rightElbow, rightWrist);
            gaitArms = new Vector3(
                leftFeature - rightFeature,
                leftWrist.Y - floorY,
                rightWrist.Y - floorY);
            return WorldLandmark.IsFinite(gaitArms);
        }

        private void SendContinuousBodyTrackerFrameOsc(
            BodyTrackerFrame frame,
            uint outputSourceEpoch,
            Vector3? gaitArms,
            CameraLowerBodyClassification legClassification)
        {
            if (_bodyOsc == null) return;

            var messages = new List<(string Address, float X, float Y, float Z)>(22);

            if (gaitArms.HasValue)
            {
                Vector3 arms = gaitArms.Value;
                messages.Add(("/tracking/gait/arms", arms.X, arms.Y, arms.Z));
            }

            // Keep gait/action semantics in the same OSC bundle and source
            // frame as the World3D tracker poses. Invalid is sent explicitly;
            // otherwise a stale kick/WalkStep from an earlier frame could
            // survive a confidence dropout in the native receiver.
            messages.Add(("/tracking/gait/leg/left",
                (float)legClassification.Left.TransportState,
                legClassification.Left.KneeAngleDegrees,
                legClassification.Left.Confidence));
            messages.Add(("/tracking/gait/leg/right",
                (float)legClassification.Right.TransportState,
                legClassification.Right.KneeAngleDegrees,
                legClassification.Right.Confidence));

            if (frame.Head.PositionValid)
            {
                Vector3 p = frame.Head.Position;
                messages.Add(("/tracking/trackers/head/position", p.X, p.Y, p.Z));
            }
            if (frame.Head.RotationValid)
            {
                Vector3 e = QuaternionToUnityEulerDegrees(frame.Head.Orientation);
                messages.Add(("/tracking/trackers/head/rotation", e.X, e.Y, e.Z));
            }

            for (int slot = 1; slot <= BodyTrackerFrame.TrackerSlotCount; slot++)
            {
                BodyTrackerPose tracker = frame.GetTracker(slot);
                if (tracker.PositionValid)
                {
                    Vector3 p = tracker.Position;
                    messages.Add(($"/tracking/trackers/{slot}/position", p.X, p.Y, p.Z));
                }
                if (tracker.RotationValid)
                {
                    Vector3 e = QuaternionToUnityEulerDegrees(tracker.Orientation);
                    messages.Add(($"/tracking/trackers/{slot}/rotation", e.X, e.Y, e.Z));
                }
            }

            uint poseMask = frame.OscPoseMask;
            OscTrackerFrameFlags flags = OscTrackerFrameContract.BuildPolicyFlags(
                frame,
                // Continuously solve HMD yaw minus measured body yaw. If the
                // camera sees the turn they cancel; if it misses the torso
                // turn, the lower body still follows the headset.
                followHmdYaw: false,
                // Camera-emulated feet must yield to VRIK's planted walk cycle
                // during stick/WIP locomotion. Physical trackers bypass this
                // policy in the native runtime, and confirmed kick feet remain
                // live through semantic arbitration.
                allowGaitFootRelease: true);
            Vector3 header = OscTrackerFrameContract.PackHeader(
                outputSourceEpoch, poseMask, flags);
            messages.Add((OscTrackerFrameContract.Address,
                header.X, header.Y, header.Z));
            SendOscBundle(messages);
        }

        // Direct OCU camera-body transport. The Configurator sends MediaPipe's
        // untouched hip-centred world landmarks in one atomic UDP datagram.
        // Axis mapping, floor placement, body basis and virtual tracker poses
        // are owned exactly once by the native runtime. This deliberately keeps
        // UI/preview code in C# without putting C# coordinate math in the game
        // tracking path.
        private void SendRawMediaPipeFramePacket(
            WorldLandmarkFrame frame,
            uint outputSourceEpoch,
            Vector3? gaitArms,
            CameraLowerBodyClassification legClassification)
        {
            const int headerBytes = 72;
            const int landmarkBytes = 16;
            const int packetBytes = headerBytes
                + WorldLandmarkFrame.MediaPipeLandmarkCount * landmarkBytes;
            var packet = new byte[packetBytes];
            packet[0] = (byte)'O';
            packet[1] = (byte)'C';
            packet[2] = (byte)'U';
            packet[3] = (byte)'3';

            void U32(int offset, uint value) =>
                BinaryPrimitives.WriteUInt32LittleEndian(
                    packet.AsSpan(offset, 4), value);
            void I32(int offset, int value) =>
                BinaryPrimitives.WriteInt32LittleEndian(
                    packet.AsSpan(offset, 4), value);
            void I64(int offset, long value) =>
                BinaryPrimitives.WriteInt64LittleEndian(
                    packet.AsSpan(offset, 8), value);
            void F32(int offset, float value) =>
                BinaryPrimitives.WriteInt32LittleEndian(
                    packet.AsSpan(offset, 4), BitConverter.SingleToInt32Bits(value));

            U32(4, 1); // protocol version
            U32(8, outputSourceEpoch);
            uint flags = frame.InputMirrored ? 1u : 0u;
            if (gaitArms.HasValue) flags |= 1u << 1;
            U32(12, flags);
            I64(16, frame.Sequence);
            I64(24, frame.TimestampMs);
            I32(32, (int)legClassification.Left.TransportState);
            I32(36, (int)legClassification.Right.TransportState);
            F32(40, legClassification.Left.KneeAngleDegrees);
            F32(44, legClassification.Left.Confidence);
            F32(48, legClassification.Right.KneeAngleDegrees);
            F32(52, legClassification.Right.Confidence);
            Vector3 arms = gaitArms ?? Vector3.Zero;
            F32(56, arms.X);
            F32(60, arms.Y);
            F32(64, arms.Z);
            U32(68, 0);

            for (int i = 0; i < WorldLandmarkFrame.MediaPipeLandmarkCount; i++)
            {
                WorldLandmark landmark = frame[i];
                int offset = headerBytes + i * landmarkBytes;
                Vector3 world = landmark.WorldPosition;
                F32(offset, world.X);
                F32(offset + 4, world.Y);
                F32(offset + 8, world.Z);
                F32(offset + 12, landmark.IsValid ? landmark.Confidence : -1f);
            }

            lock (_bodyOscGate)
            {
                try
                {
                    _bodyOsc?.Send(packet, packet.Length);
                }
                catch { }
            }
        }

        private void SendEmptyBodyTrackerFrame(uint outputSourceEpoch)
        {
            Vector3 header = OscTrackerFrameContract.PackHeader(
                outputSourceEpoch,
                poseMask: 0,
                OscTrackerFrameFlags.None);
            SendOscVec(OscTrackerFrameContract.Address,
                header.X, header.Y, header.Z);
        }

        private void SendContinuousGaitArms(Vector3 gaitArms)
        {
            SendOscVec("/tracking/gait/arms", gaitArms.X, gaitArms.Y, gaitArms.Z);
        }

        // Inverse of the DLL's Unity ordering:
        // Quaternion.Euler(x,y,z) = Qy(y) * Qx(x) * Qz(z).
        // Keeping this exact matters for foot orientation; a generic XYZ
        // extractor silently swaps the order near a raised horizontal leg.
        private static Vector3 QuaternionToUnityEulerDegrees(Quaternion value)
        {
            if (!BodyTrackerPose.IsFinite(value)
                || value.LengthSquared() <= 1e-12f)
                return Vector3.Zero;

            value = Quaternion.Normalize(value);
            float sinX = Math.Clamp(
                2f * (value.W * value.X - value.Y * value.Z), -1f, 1f);
            float x = MathF.Asin(sinX);
            float cosX = MathF.Cos(x);
            float y;
            float z;
            if (MathF.Abs(cosX) > 1e-5f)
            {
                y = MathF.Atan2(
                    2f * (value.X * value.Z + value.W * value.Y),
                    1f - 2f * (value.X * value.X + value.Y * value.Y));
                z = MathF.Atan2(
                    2f * (value.X * value.Y + value.W * value.Z),
                    1f - 2f * (value.X * value.X + value.Z * value.Z));
            }
            else
            {
                // At +/-90 degrees X, Y and Z are not independently unique.
                // Choose Z=0 and retain the equivalent combined Y rotation.
                float r01 = 2f * (value.X * value.Y - value.W * value.Z);
                float r00 = 1f - 2f * (value.Y * value.Y + value.Z * value.Z);
                y = MathF.Atan2(sinX >= 0f ? r01 : -r01, r00);
                z = 0f;
            }

            const float toDegrees = 180f / MathF.PI;
            return new Vector3(x * toDegrees, y * toDegrees, z * toDegrees);
        }

        private void SendOscVec(string address, float x, float y, float z)
        {
            lock (_bodyOscGate)
            {
                if (_bodyOsc == null) return;
                SendOscVecLocked(_bodyOsc, address, x, y, z);
            }
        }

        private void SendOscVecLocked(
            UdpClient osc,
            string address,
            float x,
            float y,
            float z)
        {
            try
            {
                byte[] packet = BuildOscVecPacket(address, x, y, z);
                osc.Send(packet, packet.Length);
            }
            catch { }
        }

        // expectedSession == null is StopBodyCapture claiming whichever session
        // is still current after its bounded join. A non-null value is the
        // capture thread proving it still owns the published socket.
        private UdpClient? ClearBodyOscSession(UdpClient? expectedSession)
        {
            lock (_bodyOscGate)
            {
                UdpClient? current = _bodyOsc;
                if (current == null
                    || (expectedSession != null
                        && !ReferenceEquals(current, expectedSession)))
                    return null;

                uint stopEpoch = (uint)(Environment.TickCount64
                    % OscTrackerFrameContract.MaximumExactFloatInteger);
                if (stopEpoch == 0) stopEpoch = 1;
                Vector3 header = OscTrackerFrameContract.PackHeader(
                    stopEpoch,
                    poseMask: 0,
                    OscTrackerFrameFlags.None);
                SendOscVecLocked(current, OscTrackerFrameContract.Address,
                    header.X, header.Y, header.Z);
                _bodyOsc = null;
                return current;
            }
        }

        private byte[] BuildOscVecPacket(string address, float x, float y, float z)
        {
            if (!_bodyOscPackets.TryGetValue(address, out byte[]? packet))
            {
                int addressBytes = System.Text.Encoding.ASCII.GetByteCount(address);
                int addressField = (addressBytes + 1 + 3) & ~3;
                const int typeField = 8; // ",fff\0" rounded to four-byte alignment
                packet = new byte[addressField + typeField + 12];
                System.Text.Encoding.ASCII.GetBytes(address, 0, address.Length, packet, 0);
                packet[addressField] = (byte)',';
                packet[addressField + 1] = (byte)'f';
                packet[addressField + 2] = (byte)'f';
                packet[addressField + 3] = (byte)'f';
                _bodyOscPackets[address] = packet;
            }

            int valuesOffset = packet.Length - 12;
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(valuesOffset, 4), BitConverter.SingleToInt32Bits(x));
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(valuesOffset + 4, 4), BitConverter.SingleToInt32Bits(y));
            BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(valuesOffset + 8, 4), BitConverter.SingleToInt32Bits(z));
            return packet;
        }

        private void SendOscBundle(
            IReadOnlyList<(string Address, float X, float Y, float Z)> messages)
        {
            lock (_bodyOscGate)
            {
                try
                {
                    if (_bodyOsc == null || messages.Count == 0) return;
                    var packets = new byte[messages.Count][];
                    int bundleLength = 16; // "#bundle\0" + immediate timetag
                    for (int i = 0; i < messages.Count; i++)
                    {
                        var message = messages[i];
                        byte[] source = BuildOscVecPacket(
                            message.Address, message.X, message.Y, message.Z);
                        packets[i] = (byte[])source.Clone();
                        bundleLength = checked(bundleLength + 4 + source.Length);
                    }

                    byte[] bundle = new byte[bundleLength];
                    System.Text.Encoding.ASCII.GetBytes("#bundle", 0, 7, bundle, 0);
                    bundle[15] = 1; // OSC immediate timetag
                    int offset = 16;
                    foreach (byte[] packet in packets)
                    {
                        BinaryPrimitives.WriteInt32BigEndian(
                            bundle.AsSpan(offset, 4), packet.Length);
                        offset += 4;
                        packet.CopyTo(bundle, offset);
                        offset += packet.Length;
                    }
                    _bodyOsc.Send(bundle, bundle.Length);
                }
                catch { }
            }
        }
    }

    // ── Silhouette panel ─────────────────────────────────────────────────
    // Simple standing human outline; tracker dots light green when the
    // matching keypoint tracks. KAT treadmill = puck under the feet.
    internal class BodySilhouettePanel : Panel
    {
        private float[,] _kp = new float[133, 3];
        private bool _valid;
        private bool _kat;

        public BodySilhouettePanel() { DoubleBuffered = true; }

        public void UpdateState(float[,] kp, bool valid, bool kat)
        {
            Array.Clear(_kp);
            Array.Copy(kp, _kp, Math.Min(kp.Length, _kp.Length));
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
            bool leftFoot = KpOk(15) && (KpOk(19) || KpOk(17) || KpOk(18));
            bool rightFoot = KpOk(16) && (KpOk(22) || KpOk(20) || KpOk(21));
            Dot(cx - hipHalf, ankY, leftFoot);                                 // feet
            Dot(cx + hipHalf, ankY, rightFoot);

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
