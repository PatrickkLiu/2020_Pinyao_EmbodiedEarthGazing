using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;

[System.Serializable]
public class HeadJoystick : MonoBehaviour
{

	public GameObject player;
	public GameObject viveCameraEye;
	public GameObject viveRightController;
	public GameObject viveLeftController;
	public GameObject viveTracker;
	public AudioClip audioTrackerCalibrated;
	public AudioClip audioGo;
	//public GameObject arrow;

	public float speedSensitivity = 8;
	public float upwardSpeedScale = 3;
	public float deadZoneRadius = 0;

	/// <summary>
	/// -1 = HeadJoystick is not initialized,
	/// 0 = Head-Joystick initialized but not calibrated,
	/// 1 = Tracker calibrated using logged data,
	/// 2 = Tracker is calibrated by backrest motion,
	/// 3 = Zero point is calibrated, HeadJoystick works now...
	/// </summary>
	[HideInInspector] public int initializeStep = 0;
	bool HeadJoystickIsReady = false;

	/// <summary>
	/// If Vive tracker is calibrated with the chair
	/// </summary>
	[HideInInspector] public bool trackerIsCalibrated = false;
	bool trackerCalibrationStarted = false, forwardMotionEnabled = true, sidewaysMotionEnabled = true, upwardMotionEnabled = true;

	public bool EnableForwardMotion {
		get {
			return forwardMotionEnabled;
		}
		set {
			forwardMotionEnabled = value;
		}
	}

	public bool EnableSidewaysMotion {
		get {
			return sidewaysMotionEnabled;
		}
		set {
			sidewaysMotionEnabled = value;
		}
	}

	public bool EnableUpwardMotion {
		get {
			return upwardMotionEnabled;
		}
		set {
			upwardMotionEnabled = value;
		}
	}

	KeyCode goKey;
	Vector3 currentTrackerPosition;
	Vector3 currentTrackerOrientation;
	Vector3[] trackerCalibrationPositions = new Vector3[5];
	Vector3[] trackerCalibrationRotations = new Vector3[5];
	int foundTrackerCalibrationPositions = 0;

	//Reading/Writing Tracker Calibration data into HeadJoystick config file
	StreamReader sr;
	StreamWriter sw;

	[HideInInspector] public float chairDirection = 0f;
	float trackerYawDataErrorOffset = 0;
	float chairDirectionRadian = 0;
	float eyeNeckPitch = 43, eyeNeckDistance = .13f;
	//HMD center has an average of 0.13 meter distance from neck and 43 degrees pitch with it!

	//Tracker Pitch data to estimate center of back-rest rotation and tracker zero position
	float chairCenterAngleX1 = 0, chairCenterAngleY1 = 0, chairCenterAngleX2 = 0, chairCenterAngleY2 = 0, chairCenterRadius1 = 0, chairCenterRadius2 = 0;

	float trackerToChairZeroPointPitchRadian = 0, trackerToChairZeroPointRadius = 0;
	//Zero Point recorded data
	float trackerNeckRadius = 0, trackerNeckPitchRadian = 0, trackerNeckDeltaYawRadian = 0;

	float exponentialTransferFuntionPower = 1.53f;
	//exponential transfer function power

	Vector3 chairCenter;
	GameObject[] locatorSpheres;
	int sphereNumbers = 0;
	[HideInInspector] public Vector3 playerSpeed;

	//Detect Tracker Variables
	int viveTrackerIndex = -1, leftControllerIndex = 3, rightControllerIndex = 4;

	bool isTrackerCalibrated ()
	{
		return trackerIsCalibrated;
	}

	public bool isReady ()
	{
		return HeadJoystickIsReady;
	}

	public void calibrate (bool showTrackerZeroPosition = false)
	{
		if (!trackerCalibrationStarted) {
			Debug.Log ("Lean back if you attached tracker to a tiltable backseat, otherwise sit comfortable and press SPACE to go...");
			trackerCalibrationStarted = true;
			startTrackerCalibration ();
		} else {

			calibrateTracker ();
		
			if (trackerIsCalibrated && Input.GetKeyUp (goKey)) {
				setZeroPoint ();
				HeadJoystickIsReady = true;
			}

			if (showTrackerZeroPosition && isTrackerCalibrated ())
				showingTrackerZeroPosition ();
		}
	}

	// *** Call this method in start() *** (loads the chair profile stored in a file by chair calibration project)
	/// <summary>
	/// Initialize Head-Joystick
	/// </summary>
	public void initialize (KeyCode goKey)
	{
		this.goKey = goKey;

		try {
			sr = new StreamReader ("HeadJoystick Config.txt");
			while (!sr.EndOfStream) {
				string FileDescription = sr.ReadLine ();

				//Reading two tracker pitches from config file
				string TrackerPitchesDescription = sr.ReadLine ();
				string trackerPitches = sr.ReadLine ();
				chairCenterAngleX1 = float.Parse (trackerPitches);
				trackerPitches = sr.ReadLine ();
				chairCenterAngleX2 = float.Parse (trackerPitches);

				//Reading two tracker-chair pitches from config file
				string trackerChairPitchDescription = sr.ReadLine ();
				string trackerChairPitch = sr.ReadLine ();
				chairCenterAngleY1 = float.Parse (trackerChairPitch);
				trackerChairPitch = sr.ReadLine ();
				chairCenterAngleY2 = float.Parse (trackerChairPitch);

				//Reading two tracker-chair radius from config file
				string ChairRadiusDescription = sr.ReadLine ();
				string ChairRadius = sr.ReadLine ();
				chairCenterRadius1 = float.Parse (ChairRadius);
				ChairRadius = sr.ReadLine ();
				chairCenterRadius2 = float.Parse (ChairRadius);
				//chairCenterAngle = float.Parse (AngularDifference);
				//chairCenterAngleRadian = chairCenterAngle * Mathf.Deg2Rad;
			}
			sr.Close ();
			//Debug.Log ("Chair profile loaded: Rotation Radious = " + chairCenterRadius + " - Angular Difference = " + chairCenterAngle);
			if (!viveTracker.activeSelf) {
				Debug.Log ("Vive tracker is not turned on!");
				viveTracker = viveRightController;
			} else {
				//detect tracker after reading the HeadJoystick config file
				Debug.Log ("Detect Tracker: Rotate on the chair with tracker without controllers...");
				trackerIsCalibrated = true;
				initializeStep = 1;
			}
		} catch {
			//detect tracker if there is no HeadJoystick config file
			Debug.Log ("Detect Tracker: Rotate on the chair with tracker without controllers...");
			initializeStep = 0;
		}
		locatorSpheres = new GameObject[10];
	}

	public void calculateChairDirection ()
	{
		chairDirection = viveTracker.transform.localRotation.eulerAngles.y + trackerYawDataErrorOffset;
		//chairDirection += transform.localRotation.eulerAngles.y;
		if (chairDirection > 360)
			chairDirection -= 360;
		if (chairDirection < 0)
			chairDirection += 360;
		chairDirectionRadian = chairDirection * Mathf.Deg2Rad;
	}

	void readTrackerData ()
	{
		calculateChairDirection ();

		currentTrackerPosition = new Vector3 (viveTracker.transform.localPosition.x, viveTracker.transform.localPosition.y, viveTracker.transform.localPosition.z);
		currentTrackerOrientation = new Vector3 (viveTracker.transform.localRotation.eulerAngles.x, viveTracker.transform.localRotation.eulerAngles.y, viveTracker.transform.localRotation.eulerAngles.z);

		//**** Fixing funky tracker pitch & yaw errors in Unity!!! ***
		currentTrackerOrientation.y += trackerYawDataErrorOffset;
		if (currentTrackerOrientation.z > 90 && currentTrackerOrientation.z < 135)
			currentTrackerOrientation.x = 180 - currentTrackerOrientation.x;
	}

	bool isTrackerInNewPosition (Vector3 currentTrackerPosition, Vector3 currentTrackerRotation)
	{
		for (int i = 0; i < foundTrackerCalibrationPositions; i++)
			if (Mathf.Abs (currentTrackerRotation.x - trackerCalibrationRotations [i].x) < 2.5f)
				return false;
		return true;
	}

	/// <summary>
	/// This method calculates the back-rest rotation center of the chair using four tracker positions (with different pitches) using (W.H.Beyer, 1987) technique
	/// </summary>
	/// <returns><c>true</c>, if chair center radius and angle was calculated, <c>false</c> otherwise.</returns>
	bool calculateChairCenterRadiusAndAngle ()
	{
		Vector3 p1 = new Vector3 (trackerCalibrationPositions [1].x, trackerCalibrationPositions [1].y, trackerCalibrationPositions [1].z);
		float k1 = Mathf.Pow (Vector3.Magnitude (p1), 2);
		Vector3 p2 = new Vector3 (trackerCalibrationPositions [2].x, trackerCalibrationPositions [2].y, trackerCalibrationPositions [2].z);
		float k2 = Mathf.Pow (Vector3.Magnitude (p2), 2);
		Vector3 p3 = new Vector3 (trackerCalibrationPositions [3].x, trackerCalibrationPositions [3].y, trackerCalibrationPositions [3].z);
		float k3 = Mathf.Pow (Vector3.Magnitude (p3), 2);
		Vector3 p4 = new Vector3 (trackerCalibrationPositions [4].x, trackerCalibrationPositions [4].y, trackerCalibrationPositions [4].z);
		float k4 = Mathf.Pow (Vector3.Magnitude (p4), 2);

		Vector4 col1 = new Vector4 (p1.x, p1.y, p1.z, 1);
		Vector4 col2 = new Vector4 (p2.x, p2.y, p2.z, 1);
		Vector4 col3 = new Vector4 (p3.x, p3.y, p3.z, 1);
		Vector4 col4 = new Vector4 (p4.x, p4.y, p4.z, 1);
		Matrix4x4 matrix4x4 = new Matrix4x4 (col1, col2, col3, col4);
		float determinantDR = Matrix4x4.Determinant (matrix4x4);

		col1 = new Vector4 (k1, p1.y, p1.z, 1);
		col2 = new Vector4 (k2, p2.y, p2.z, 1);
		col3 = new Vector4 (k3, p3.y, p3.z, 1);
		col4 = new Vector4 (k4, p4.y, p4.z, 1);
		matrix4x4 = new Matrix4x4 (col1, col2, col3, col4);
		float determinantDX = Matrix4x4.Determinant (matrix4x4);

		col1 = new Vector4 (k1, p1.x, p1.z, 1);
		col2 = new Vector4 (k2, p2.x, p2.z, 1);
		col3 = new Vector4 (k3, p3.x, p3.z, 1);
		col4 = new Vector4 (k4, p4.x, p4.z, 1);
		matrix4x4 = new Matrix4x4 (col1, col2, col3, col4);
		float determinantDY = Matrix4x4.Determinant (matrix4x4);

		col1 = new Vector4 (k1, p1.x, p1.y, 1);
		col2 = new Vector4 (k2, p2.x, p2.y, 1);
		col3 = new Vector4 (k3, p3.x, p3.y, 1);
		col4 = new Vector4 (k4, p4.x, p4.y, 1);
		matrix4x4 = new Matrix4x4 (col1, col2, col3, col4);
		float determinantDZ = Matrix4x4.Determinant (matrix4x4);

		col1 = new Vector4 (k1, p1.x, p1.y, p1.z);
		col2 = new Vector4 (k2, p2.x, p2.y, p2.z);
		col3 = new Vector4 (k3, p3.x, p3.y, p3.z);
		col4 = new Vector4 (k4, p4.x, p4.y, p4.z);
		matrix4x4 = new Matrix4x4 (col1, col2, col3, col4);
		float determinantDI = Matrix4x4.Determinant (matrix4x4);

		Vector3 O = new Vector3 (-determinantDX / 2 * determinantDR, determinantDY / 2 * determinantDR, -determinantDZ / 2 * determinantDR);
		//showSphere (O, 0, true);
		chairCenterRadius1 = Vector3.Distance (p1, O);
		chairCenterRadius2 = Vector3.Distance (p4, O);
		float chairCenterRadius3 = Vector3.Distance (p2, O);
		float chairCenterRadius4 = Vector3.Distance (p3, O);
		Debug.Log ("P1-4 distance from center of chair rotation = " + chairCenterRadius1 + " , " + chairCenterRadius3 + " , " + chairCenterRadius4 + " , " + chairCenterRadius2);
		//Calculate chair-center vs tracker delta pitch based on p1 & p4
		chairCenterAngleX1 = trackerCalibrationRotations [1].x;
		chairCenterAngleX2 = trackerCalibrationRotations [4].x;
		chairCenterAngleY1 = Mathf.Acos ((p1.y - O.y) / chairCenterRadius1) * Mathf.Rad2Deg;
		chairCenterAngleY2 = Mathf.Acos ((p4.y - O.y) / chairCenterRadius2) * Mathf.Rad2Deg;
		Debug.Log ("X1 => Y1: (" + chairCenterAngleX1 + "=>" + chairCenterAngleY1 + ") - X2 => Y2: (" + chairCenterAngleX2 + "=>" + chairCenterAngleY2 + ")");

		if (currentTrackerOrientation.y > 90 && currentTrackerOrientation.y < 270) {
			Debug.Log ("Tracker yaw in Unity has 180 degrees error!");
			trackerYawDataErrorOffset = 180;
		}
		if (Mathf.Abs (chairCenterAngleY1 - chairCenterAngleY2) < 3 || Mathf.Abs (chairCenterRadius1 - chairCenterRadius2) > .1f)
			return false;
		else
			return true;
	}

	public void startTrackerCalibration ()
	{
		foundTrackerCalibrationPositions = 0;
		initializeStep = 1;
	}

	/// <summary>
	/// Calibrates the tracker either from the log file of previous application executions or by forward/backward movement of the chair backrest.
	/// </summary>
	public void calibrateTracker ()
	{
		if ((initializeStep == 0 || initializeStep == 1) && (foundTrackerCalibrationPositions < 5)) {
			readTrackerData ();
			if (isTrackerInNewPosition (currentTrackerPosition, currentTrackerOrientation)) {
				trackerCalibrationPositions [foundTrackerCalibrationPositions] = new Vector3 (currentTrackerPosition.x, currentTrackerPosition.y, currentTrackerPosition.z);
				trackerCalibrationRotations [foundTrackerCalibrationPositions] = new Vector3 (currentTrackerOrientation.x, currentTrackerOrientation.y, currentTrackerOrientation.z);
				foundTrackerCalibrationPositions++;
				//Debug.Log ("Total found tracker positions = " + foundTrackerCalibrationPositions);
				if (foundTrackerCalibrationPositions == 5) {//If we have four tracker data to calibrate the center of the chair rotation
					Debug.Log ("Five points Yaw   = " + trackerCalibrationRotations [0].z.ToString ("F0") + ", " + trackerCalibrationRotations [1].z.ToString ("F0") + ", " + trackerCalibrationRotations [2].x.ToString ("F0") + ", " + trackerCalibrationRotations [3].z.ToString ("F0") + ", " + trackerCalibrationRotations [4].z.ToString ("F0"));
					Debug.Log ("Five points pitch = " + trackerCalibrationRotations [0].x.ToString ("F0") + ", " + trackerCalibrationRotations [1].x.ToString ("F0") + ", " + trackerCalibrationRotations [2].x.ToString ("F0") + ", " + trackerCalibrationRotations [3].x.ToString ("F0") + ", " + trackerCalibrationRotations [4].x.ToString ("F0"));
					Debug.Log ("Five points Roll  = " + trackerCalibrationRotations [0].y.ToString ("F0") + ", " + trackerCalibrationRotations [1].y.ToString ("F0") + ", " + trackerCalibrationRotations [2].x.ToString ("F0") + ", " + trackerCalibrationRotations [3].y.ToString ("F0") + ", " + trackerCalibrationRotations [3].y.ToString ("F0"));
					if (calculateChairCenterRadiusAndAngle ()) {
						Debug.Log ("TrackerIsCalibrated! Chair center radius = [" + chairCenterRadius1.ToString ("F3") + ".." + chairCenterRadius2.ToString ("F3") + "]");
						sw = new StreamWriter ("HeadJoystick Config.txt", true);
						sw.WriteLine ("*** Chair Calibration Data to interpolate the backrest rotation center ***");
						sw.WriteLine ("Vive tracker pitches:");
						sw.WriteLine (chairCenterAngleX1);
						sw.WriteLine (chairCenterAngleX2);
						sw.WriteLine ("Vive tracker pitches related to the center of the back-rest rotation:");
						sw.WriteLine (chairCenterAngleY1);
						sw.WriteLine (chairCenterAngleY2);
						sw.WriteLine ("Distances between tracker and center of the back-rest rotation for each pitch:");
						sw.WriteLine (chairCenterRadius1);
						sw.WriteLine (chairCenterRadius2);
						sw.Close ();
						player.GetComponent<AudioSource> ().PlayOneShot (audioTrackerCalibrated);
						trackerIsCalibrated = true;
						initializeStep = 2;
						Debug.Log ("Tracker calibrated using rotation data! Ask the user to sit comfortable and look forward and then press " + goKey.ToString ());
					} else {
						Debug.Log ("Tracker calibration Error! Tracker-Chair Pitch data is corrupted in range [" + chairCenterAngleY1.ToString ("F1") + " .. " + chairCenterAngleY1.ToString ("F1") + "] . PLease push chair backrest forward/backward again!");
						startTrackerCalibration ();
					}
				}
			}
		}
	}

	float getChairTrackerPitch (float trackerPitch)
	{
		float a = (trackerPitch - chairCenterAngleX1) / (chairCenterAngleX2 - chairCenterAngleX1);
		return chairCenterAngleY1 + a * (chairCenterAngleY2 - chairCenterAngleY1);
	}

	float getChairCenterRadius (float trackerPitch)
	{
		float a = (trackerPitch - chairCenterAngleX1) / (chairCenterAngleX2 - chairCenterAngleX1);
		return chairCenterRadius1 + a * (chairCenterRadius2 - chairCenterRadius1);
	}

	void showSphere (Vector3 targetPosition, int sphere = 0, bool destroyable = false)
	{
		if (locatorSpheres [sphere] == null) {
			locatorSpheres [sphere] = GameObject.CreatePrimitive (PrimitiveType.Sphere);
			locatorSpheres [sphere].GetComponent<Renderer> ().material.color = Random.ColorHSV ();
		}
		locatorSpheres [sphere].transform.SetParent (transform);
		locatorSpheres [sphere].name = "Cockpit";
		locatorSpheres [sphere].transform.localPosition = new Vector3 (targetPosition.x, targetPosition.y, targetPosition.z);
		locatorSpheres [sphere].transform.localScale = new Vector3 (.1f, .1f, .1f);
		if (destroyable)
			GameObject.Destroy (locatorSpheres [sphere], 5);
	}

	/// <summary>
	/// Shows the chair center.
	/// </summary>
	public void showingTrackerZeroPosition ()
	{
		
		//calculateChairDirection ();
		readTrackerData ();

		float chairTrackerPitchRadian = getChairTrackerPitch (currentTrackerOrientation.x) * Mathf.Deg2Rad;
		float chairCenterRadius = getChairCenterRadius (currentTrackerOrientation.x);

		float pitchSin = Mathf.Sin (chairTrackerPitchRadian), pitchCos = Mathf.Cos (chairTrackerPitchRadian);
		float yawSin = Mathf.Sin (chairDirectionRadian), yawCos = Mathf.Cos (chairDirectionRadian);

		Vector3 O = new Vector3 ();
		O.x = currentTrackerPosition.x + chairCenterRadius * pitchSin * yawSin;
		O.y = currentTrackerPosition.y - chairCenterRadius * pitchCos;
		O.z = currentTrackerPosition.z + chairCenterRadius * pitchSin * yawCos;

		float zeroPitchSin = Mathf.Sin (trackerToChairZeroPointPitchRadian), zeroPitchCos = Mathf.Cos (trackerToChairZeroPointPitchRadian);

		Vector3 T0 = new Vector3 ();
		T0.x = currentTrackerPosition.x + chairCenterRadius * yawSin * (pitchSin - zeroPitchSin);
		T0.y = currentTrackerPosition.y - chairCenterRadius * (pitchCos - zeroPitchCos);
		T0.z = currentTrackerPosition.z + chairCenterRadius * yawCos * (pitchSin - zeroPitchSin);

		if (initializeStep > 1) {
			showSphere (O, 1);
			//showSphere (T0, 2);
			//if(Mathf.Abs(trackerToChairZeroPointPitchRadian * Mathf.Rad2Deg) > 1)
			//	Debug.Log ("zero Pitch = " + trackerToChairZeroPointPitchRadian * Mathf.Rad2Deg + " - tracker-chair radius = " + chairCenterRadius.ToString("F2") + " pitch = " + chairTrackerPitchRadian * Mathf.Rad2Deg + " - yaw = " + chairDirectionRadian * Mathf.Rad2Deg + " - O = " + O.ToString("F2") + " - T0 = " + T0.ToString("F2"));
		}
	}

	Vector3 findTrackerZeroPosition ()
	{
		//calculateChairDirection ();
		readTrackerData ();

		float chairTrackerPitchRadian = getChairTrackerPitch (currentTrackerOrientation.x) * Mathf.Deg2Rad;
		float chairCenterRadius = getChairCenterRadius (currentTrackerOrientation.x);

		float pitchSin = Mathf.Sin (chairTrackerPitchRadian), pitchCos = Mathf.Cos (chairTrackerPitchRadian);
		float yawSin = Mathf.Sin (chairDirectionRadian), yawCos = Mathf.Cos (chairDirectionRadian);

		float zeroPitchSin = Mathf.Sin (trackerToChairZeroPointPitchRadian), zeroPitchCos = Mathf.Cos (trackerToChairZeroPointPitchRadian);

		Vector3 T0 = new Vector3 ();
		T0.x = currentTrackerPosition.x + chairCenterRadius * yawSin * (pitchSin - zeroPitchSin);
		T0.y = currentTrackerPosition.y - chairCenterRadius * (pitchCos - zeroPitchCos);
		T0.z = currentTrackerPosition.z + chairCenterRadius * yawCos * (pitchSin - zeroPitchSin);
		return T0;
		//if (initializeStep == 2)
		//	Debug.Log ("Chair Direction = " + chairDirectionRadian * Mathf.Rad2Deg + " - Radius = " + chairCenterRadius + " - TO Pitch = " + chairTrackerPitchRadian * Mathf.Rad2Deg + " - Tracker = " + viveTracker.transform.position.ToString () + " - Center = " + O.ToString ());
	}

	Vector3 getNeckPosition ()
	{
		//Average Head-Neck vector has 0.08 meter length and 48 degrees pitch
		float headPitch = (viveCameraEye.transform.localRotation.eulerAngles.x - eyeNeckPitch) * Mathf.Deg2Rad;
		float headYaw = viveCameraEye.transform.localRotation.eulerAngles.y * Mathf.Deg2Rad;

		float headWidth = eyeNeckDistance * Mathf.Cos (headPitch);
		float headHeight = eyeNeckDistance * Mathf.Sin (headPitch);

		Vector3 neck = new Vector3 ();
		neck.x = viveCameraEye.transform.localPosition.x - headWidth * Mathf.Sin (headYaw); //Calculate the Neck x Position
		neck.y = viveCameraEye.transform.localPosition.y + headHeight; //Calculate the Neck y Position based on negative headHeight
		neck.z = viveCameraEye.transform.localPosition.z - headWidth * Mathf.Cos (headYaw); //Calculate the Neck y Position
		//Debug.Log("Neck = " + neck.ToString("F3") + " - Head Pitch = " + viveCameraEye.transform.localRotation.eulerAngles.x + " - Yaw = " + viveCameraEye.transform.localRotation.eulerAngles.y + " - Roll = " + viveCameraEye.transform.localRotation.eulerAngles.z);
		return neck;
	}

	void calculateTrackerNeckVector (Vector3 neck, Vector3 trackerZeroPosition)
	{
		Vector3 T0N0 = new Vector3 (neck.x - trackerZeroPosition.x, neck.y - trackerZeroPosition.y, neck.z - trackerZeroPosition.z);
		trackerNeckRadius = T0N0.magnitude;
		trackerNeckPitchRadian = Mathf.Asin (T0N0.y / trackerNeckRadius);
		float T0N0Yaw = Mathf.Atan2 (T0N0.x, T0N0.z);
		trackerNeckDeltaYawRadian = chairDirectionRadian - T0N0Yaw;
		if (trackerNeckDeltaYawRadian < 0)
			trackerNeckDeltaYawRadian += Mathf.PI * 2;
		if (trackerNeckDeltaYawRadian > Mathf.PI * 2)
			trackerNeckDeltaYawRadian -= Mathf.PI * 2;
		//Debug.Log ("T0N0 = " + T0N0.ToString () + " - Distance = " + trackerNeckRadius.ToString ("F2") + " - Pitch = " + trackerNeckPitchRadian * Mathf.Rad2Deg + " deltaTetta = " + trackerNeckDeltaYawRadian * Mathf.Rad2Deg);
	}

	void recordTrackerZeroPointPosition ()
	{
		trackerToChairZeroPointPitchRadian = getChairTrackerPitch (currentTrackerOrientation.x) * Mathf.Deg2Rad;
		trackerToChairZeroPointRadius = getChairCenterRadius (currentTrackerOrientation.x);
	}

	/// <summary>
	/// Head position calibrated as the zero point and the motion starts
	/// </summary>
	public void setZeroPoint ()
	{
		if (initializeStep == 1 || initializeStep == 2) {
			//calculateChairDirection ();
			readTrackerData ();
			recordTrackerZeroPointPosition ();
			Vector3 trackerZeroPosition = findTrackerZeroPosition ();
			Vector3 neck = getNeckPosition ();
			//showSphere (neck, 2, true);
			calculateTrackerNeckVector (neck, trackerZeroPosition);
			Debug.Log ("Now the user can move...");
			player.GetComponent<AudioSource> ().PlayOneShot (audioGo);
			initializeStep = 3;
		}
	}

	// *** Call this method in update() *** (Uses the Vive HMD & Controller data stored in internal variables to move the player in Virtual Environment)
	/// <summary>
	/// Moving player.
	/// </summary>
	/// <param name="forizontalVelocityLimit">Forward/Sideways velocity limit.</param>
	/// <param name="verticalVelocityLimit">Upward/Downward velocity limit.</param>
	/// <param name="activateExponentialTransferFunction">If set to <c>true</c> show tracker zero position in the game to ensure it is stable visually.</param>
	public void move (float horizontalVelocityLimit, float verticalVelocityLimit, bool showTrackerZeroPosition = false)
	{

		if (showTrackerZeroPosition && isTrackerCalibrated ())
			showingTrackerZeroPosition ();
		
		//******************************** Read Tracker and find tracker zero position ********************************
		readTrackerData ();
		Vector3 trackerZeroPosition = findTrackerZeroPosition ();

		//************************ Tracker Zero Position => Neck Zero Position *******************
		float trackerNeckYawRadian = chairDirectionRadian - trackerNeckDeltaYawRadian;
		if (trackerNeckYawRadian < 0)
			trackerNeckYawRadian += Mathf.PI * 2;
		if (trackerNeckYawRadian > Mathf.PI * 2)
			trackerNeckYawRadian -= Mathf.PI * 2;

		Vector3 trackerNeckVector = new Vector3 ();
		trackerNeckVector.x = trackerNeckRadius * Mathf.Cos (trackerNeckPitchRadian) * Mathf.Sin (trackerNeckYawRadian);
		trackerNeckVector.y = trackerNeckRadius * Mathf.Sin (trackerNeckPitchRadian);
		trackerNeckVector.z = trackerNeckRadius * Mathf.Cos (trackerNeckPitchRadian) * Mathf.Cos (trackerNeckYawRadian);

		Vector3 neck0 = trackerZeroPosition + trackerNeckVector;
		//Debug.Log ("Tracker Zero position = " + trackerZeroPosition.ToString("F2") + " - Neck Vector = " + trackerNeckVector.ToString("F2") + " - neck0 = " + neck0.ToString ("F2") + " - neck = " + neck.ToString ("F2"));
		//showSphere (neck0, 4);

		//********************* Neck Displacement = Current Neck Position - Neck Zero Position ************************
		Vector3 neck = getNeckPosition ();
		Vector3 neckDisplacement = neck - neck0;
		// **************************** Speed = neck displacement * Sensitivity ***************************************
		neckDisplacement *= speedSensitivity;
		neckDisplacement.y *= upwardSpeedScale;

		// *********************** Calculate polar coordinates of the neck displacement *******************************
		float velocity = neckDisplacement.magnitude;
		//Debug.Log ("distance = " + velocity);
		velocity -= deadZoneRadius; // Apply dead-zone
		if (velocity < 0)
			velocity = 0;
		float neckDisplacementMinusDeadZoneRadiusY = (neckDisplacement.magnitude == 0) ? 0 : neckDisplacement.y * velocity / neckDisplacement.magnitude;
		float speedPitchRadian = (velocity == 0) ? 0 : Mathf.Asin ((float)(neckDisplacementMinusDeadZoneRadiusY / velocity)); // Fi in radian
		float speedYawRadian = (neckDisplacement.x == 0 && neckDisplacement.z == 0) ? 0 : Mathf.Atan2 (neckDisplacement.x, neckDisplacement.z); //Tetta in radian

		//speedYawRadian += transform.localRotation.eulerAngles.y;

		//Debug.Log ("Neck displacement = " + neckDisplacement.ToString ("F2") + "velocity = " + velocity.ToString ("F2") + " - pitch = " + speedPitchRadian * Mathf.Rad2Deg + " - Yaw = " + speedYawRadian * Mathf.Rad2Deg);
		//************************ Applying Exponential Transfer Function *********************************************
		float velocityExp = Mathf.Pow (velocity, exponentialTransferFuntionPower);

		// *********************************** Apply speed limit ******************************************************	`
		float velocityX = velocityExp * Mathf.Cos (speedPitchRadian) * Mathf.Sin (speedYawRadian);
		float velocityY = velocityExp * Mathf.Sin (speedPitchRadian);
		float velocityZ = velocityExp * Mathf.Cos (speedPitchRadian) * Mathf.Cos (speedYawRadian);

		if (verticalVelocityLimit >= 0 && Mathf.Abs (velocityY) > verticalVelocityLimit)
			velocityY = (velocityY > 0) ? verticalVelocityLimit : -verticalVelocityLimit;
		
		Vector2 horizontalSpeed = new Vector2 (velocityX, velocityZ);
		float horizontalVelocity = horizontalSpeed.magnitude;
		float speedVectorYawRadian = (velocityX == 0 && velocityZ == 0) ? 0 : Mathf.Atan2 (velocityX, velocityZ);
		if (horizontalVelocityLimit >= 0 && horizontalVelocity > horizontalVelocityLimit) {
			velocityX = horizontalVelocityLimit * Mathf.Sin (speedVectorYawRadian);
			velocityZ = horizontalVelocityLimit * Mathf.Cos (speedVectorYawRadian);
		}

		// ************************************ Move the user *********************************************************
		if (!sidewaysMotionEnabled)
			velocityX = 0;
		if (!upwardMotionEnabled)
			velocityY = 0;
		if (!forwardMotionEnabled)
			velocityZ = 0;
		
		playerSpeed = new Vector3 (velocityX, velocityY, velocityZ);
		Vector3 playerTranslate = playerSpeed * Time.deltaTime;

		//Debug.Log ("neck displacement.x = " + neckDisplacement.x + " - Velocity Exp = " + velocityExp + " - Velocity = " + velocity + " vertical neck displacement = " + neckDisplacement.y + " - Fi = " + speedPitchRadian + " - Tetta = " + speedYawRadian + " - velocityX = " + velocityX + " - Speed.x = " + horizontalSpeed.x);
		if (initializeStep == 3) {
			//Debug.Log ("Player Translate: X = " + playerTranslate.x + " - Y = " + playerTranslate.y + " - Z = " + playerTranslate.z);
			player.transform.Translate (playerTranslate, Space.Self);

		}
	}
}
