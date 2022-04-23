using System;
using System.Collections;
using System.Collections.Generic;
using System.Security.AccessControl;
using System.Timers;
using UnityEngine;
using UnityEngine.UI;

public class FirstPersonController : MonoBehaviour
{
	#region variables

	private bool canMove = true;
	public bool isSprinting => canSprint && Input.GetKey(sprintKey);
	private bool shouldJump => characterController.isGrounded && Input.GetKey(jumpKey);
	private bool shouldCrouch => Input.GetKey(crouchKey) && !duringCrouchAnimation && characterController.isGrounded;

	[Header("Functional Options")] [SerializeField]
	private bool canSprint = true;

	private bool canJump = true;
	private bool canCrouch = true;
	private bool canADS = true;
	private bool canHeadBob = true;
	private bool willSlideDownSlopes = true;

	[Header("Controls")] [SerializeField] private KeyCode sprintKey = KeyCode.LeftShift;
	[SerializeField] private KeyCode jumpKey = KeyCode.Space;
	[SerializeField] private KeyCode crouchKey = KeyCode.LeftControl;

	[Header("Movement Params")] [SerializeField]
	private float walkSpeed = 3f;

	[SerializeField] private float crouchSpeed = 1.5f;
	[SerializeField] private float sprintSpeed = 6f;
	[SerializeField] private float slopeSpeed = 10f;

	[Header("Look Params")] [SerializeField, Range(1, 10)]
	private float lookSpeedX = 2f;

	[SerializeField, Range(1, 10)] private float lookSpeedY = 2f;
	[SerializeField, Range(1, 180)] private float upperLookLimit = 80f;
	[SerializeField, Range(1, 180)] private float lowerLookLimit = 80f;

	[Header("Jump Params")] [SerializeField]
	private float jumpForce = 8f;

	[SerializeField] private float gravity = 30f;

	[Header("Crouch Params")] [SerializeField]
	private float crouchHeight = 0.5f;

	[SerializeField] private float standingHeight = 1.8f;
	[SerializeField] private float timeToCrouch = 0.25f;
	[SerializeField] private Vector3 crouchingCentre = new Vector3(0, 0.5f, 0);
	[SerializeField] private Vector3 standingCentre = Vector3.zero;

	[Header("HeadBob")] [SerializeField] private float walkBobSpeed = 14f;
	[SerializeField] private float walkBobAmount = 0.05f;
	[SerializeField] private float sprintBobSpeed = 18f;
	[SerializeField] private float sprintBobAmount = 0.11f;
	[SerializeField] private float crouchBobSpeed = 8f;
	[SerializeField] private float crouchBobAmount = 0.025f;
	private float defaultYPos;
	private float headBobTimer;
	#region SlidingParams

	private Vector3 hitPointNormal;

	bool isSliding
	{
		get
		{
			if (!characterController.isGrounded ||
			    !Physics.Raycast(transform.position, Vector3.down, out RaycastHit slopeHit, 1.5f)) return false;
			hitPointNormal = slopeHit.normal;
			return Vector3.Angle(hitPointNormal, Vector3.up) > characterController.slopeLimit;
		}
	}

	#endregion
	public bool isCrouching = false;
	private bool duringCrouchAnimation = false;
	private Camera playerCamera;
	private CharacterController characterController;
	private Vector3 moveDirection;
	private Vector2 currentInput;
	private float rotationX;
	private Animator animator;
	public bool isMoving = false;
	private float inAirTimer = 0f;
	public event Action<float> OnPlayerLand; 
	public event Action OnPlayerSlide;
	private static readonly int Run = Animator.StringToHash("Run");
	private static readonly int Walk = Animator.StringToHash("Walk");
	#endregion

	private void Awake()
	{
		playerCamera = GetComponentInChildren<Camera>();
		characterController = GetComponent<CharacterController>();
		animator = GetComponentInChildren<Animator>();
		playerStats = GetComponent<PlayerStats>();
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		defaultYPos = playerCamera.transform.localPosition.y;
	
	
	}

	private void OnEnable()
	{
	}

	private void OnDisable()
	{
	}

	
	private void Update()
	{
		if (!canMove) return;
		if (!characterController.isGrounded) inAirTimer += Time.deltaTime;
		else
		{
			if (inAirTimer > 0.1f)
			{
				OnPlayerLand?.Invoke(inAirTimer);
			}
			inAirTimer = 0f;
		}

		HandleMovementInput();
		if (Mathf.Abs(moveDirection.x) > 0.1f || Mathf.Abs(moveDirection.z) > 0.1f) isMoving = true;
		else isMoving = false;
		HandleMouseLook();
		if (shouldJump) HandleJump();
		if (shouldCrouch) HandleCrouch();
		if (canHeadBob) HandleHeadBob();
		ApplyFinalMovement();
	}


	private void HandleHeadBob()
	{
		if (!characterController.isGrounded) return;
		if (isMoving)
		{
			headBobTimer += Time.deltaTime *
			                (isCrouching ? crouchBobSpeed : isSprinting ? sprintBobSpeed : walkBobSpeed);
			playerCamera.transform.localPosition = new Vector3(
				playerCamera.transform.localPosition.x,
				defaultYPos + Mathf.Sin(headBobTimer) *
				(isCrouching ? crouchBobAmount : isSprinting ? sprintBobAmount : walkBobAmount),
				playerCamera.transform.localPosition.z);
		}
	}

	private void HandleCrouch()
	{
		if (shouldCrouch) StartCoroutine(CrouchStand());
		
	}

	private IEnumerator CrouchStand()
	{
		if (isCrouching && Physics.Raycast(playerCamera.transform.position, Vector3.up,
			standingHeight - crouchHeight + 0.3f)) yield break;
		duringCrouchAnimation = true;
		float timeElapsed = 0f;
		float targetHeight = isCrouching ? standingHeight : crouchHeight;
		float currentHeight = characterController.height;
		Vector3 targetCentre = isCrouching ? standingCentre : crouchingCentre;
		Vector3 currentCentre = characterController.center;
		while (timeElapsed < timeToCrouch)
		{
			characterController.height = Mathf.Lerp(currentHeight, targetHeight, timeElapsed / timeToCrouch);
			characterController.center = Vector3.Lerp(currentCentre, targetCentre, timeElapsed / timeToCrouch);
			timeElapsed += Time.deltaTime;
			yield return null;
		}
		characterController.height = targetHeight;
		characterController.center = targetCentre;
		isCrouching = !isCrouching;
		duringCrouchAnimation = false;
	}

	private void HandleMovementInput()
	{
		currentInput = new Vector2(
			(isCrouching ? crouchSpeed : isSprinting ? sprintSpeed : walkSpeed) * Input.GetAxis("Vertical"),
			walkSpeed * Input.GetAxis("Horizontal"));
		HandleAnimation();

		float moveDirectionY = moveDirection.y;
		moveDirection = (transform.TransformDirection((Vector3.forward)) * currentInput.x +
		                 (transform.TransformDirection(Vector3.right)) * currentInput.y);
		moveDirection.y = moveDirectionY;
	}

	private void HandleAnimation()
	{
		if (isMoving)
		{
			if (isSprinting && currentInput.x > 0)
			{
				if (!animator.GetBool(Run)) animator.SetBool(Run, true);
				if (animator.GetBool(Walk)) animator.SetBool(Walk, false);
			}
			else
			{
				if (!animator.GetBool(Walk)) animator.SetBool(Walk, true);
				if (animator.GetBool(Run)) animator.SetBool(Run, false);
			}
		}
		else
		{
			if (animator.GetBool(Walk)) animator.SetBool(Walk, false);
			if (animator.GetBool(Run)) animator.SetBool(Run, false);
		}
	}

	private void HandleMouseLook()
	{
		rotationX -= Input.GetAxis("Mouse Y") * lookSpeedY;
		rotationX = Mathf.Clamp(rotationX, -upperLookLimit, lowerLookLimit);
		playerCamera.transform.localRotation = Quaternion.Euler(rotationX, 0, 0);
		transform.rotation *= Quaternion.Euler(0, Input.GetAxis("Mouse X") * lookSpeedX, 0);
	}

	private void HandleJump()
	{
		if (canJump) moveDirection.y = jumpForce;
	}

	private void ApplyFinalMovement()
	{
		if (!characterController.isGrounded) moveDirection.y -= gravity * Time.deltaTime;
		if (willSlideDownSlopes && isSliding)
		{
			moveDirection += new Vector3(hitPointNormal.x, -hitPointNormal.y, hitPointNormal.z) * slopeSpeed;
			OnPlayerSlide?.Invoke();
		}
		characterController.Move(moveDirection * Time.deltaTime);
	}
}