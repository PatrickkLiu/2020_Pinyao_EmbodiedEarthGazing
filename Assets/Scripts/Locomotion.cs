using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Locomotion : MonoBehaviour
{
	
	public float horizontalSpeedLimit = 4, verticalSpeedLimit = 0, rotationVelocity = 30;
	HeadJoystick headJoystick;

	// Use this for initialization
	void Start ()
	{
		headJoystick = GetComponent<HeadJoystick> ();
		headJoystick.initialize (KeyCode.Space);
	}
	
	// Update is called once per frame
	void Update ()
	{
		//Using HeadJoystick
		if (!headJoystick.isReady ())
			headJoystick.calibrate (true);
		else
			headJoystick.move (horizontalSpeedLimit, verticalSpeedLimit, true);

		//Rotating player by pressing left/right arrow keys if they don't want to rotate the chair
		if (Input.GetKey (KeyCode.LeftArrow))
			transform.RotateAround (headJoystick.viveCameraEye.transform.position, Vector3.up, -rotationVelocity * Time.deltaTime);
		if (Input.GetKey (KeyCode.RightArrow))
			transform.RotateAround (headJoystick.viveCameraEye.transform.position, Vector3.up, rotationVelocity * Time.deltaTime);
	}
}
