using UnityEngine;

// FPSWalkerEnhanced
// From Unify Community Wiki

// https://wiki.unity3d.com/index.php/FPSWalkerEnhanced#FPSWalkerEnhanced.cs
 
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Tooltip("How fast the player moves when walking (default move speed).")]
    [SerializeField]
    private float _WalkSpeed = 6.0f;
 
    [Tooltip("How fast the player moves when running.")]
    [SerializeField]
    private float _RunSpeed = 11.0f;
 
    [Tooltip("If true, diagonal speed (when strafing + moving forward or back) can't exceed normal move speed; otherwise it's about 1.4 times faster.")]
    [SerializeField]
    public bool _LimitDiagonalSpeed = true;
 
    [Tooltip("If checked, the run key toggles between running and walking. Otherwise player runs if the key is held down.")]
    [SerializeField]
    private bool _ToggleRun = false;
 
    [Tooltip("How high the player jumps when hitting the jump button.")]
    [SerializeField]
    private float _JumpSpeed = 8.0f;
 
    [Tooltip("How fast the player falls when not standing on anything.")]
    [SerializeField]
    private float _Gravity = 20.0f;
 
    [Tooltip("Units that player can fall before a falling function is run. To disable, type \"infinity\" in the inspector.")]
    [SerializeField]
    private float _FallingThreshold = 10.0f;
 
    [Tooltip("If the player ends up on a slope which is at least the Slope Limit as set on the character controller, then he will slide down.")]
    [SerializeField]
    private bool _SlideWhenOverSlopeLimit = false;
 
    [Tooltip("If checked and the player is on an object tagged \"Slide\", he will slide down it regardless of the slope limit.")]
    [SerializeField]
    private bool _SlideOnTaggedObjects = false;
 
    [Tooltip("How fast the player slides when on slopes as defined above.")]
    [SerializeField]
    private float _SlideSpeed = 12.0f;
 
    [Tooltip("If checked, then the player can change direction while in the air.")]
    [SerializeField]
    private bool _AirControl = false;
 
    [Tooltip("Small amounts of this results in bumping when walking down slopes, but large amounts results in falling too fast.")]
    [SerializeField]
    private float _AntiBumpFactor = .75f;
 
    [Tooltip("Player must be grounded for at least this many physics frames before being able to jump again; set to 0 to allow bunny hopping.")]
    [SerializeField]
    private int _AntiBunnyHopFactor = 1;
 
    private Vector3 _MoveDirection = Vector3.zero;
    private bool _Grounded = false;
    private CharacterController _Controller;
    private Transform _Transform;
    private float _Speed;
    private RaycastHit _Hit;
    private float _FallStartLevel;
    private bool _Falling;
    private float _SlideLimit;
    private float _RayDistance;
    private Vector3 _ContactPoint;
    private bool _PlayerControl = false;
    private int _JumpTimer;
 
 
    private void Start()
    {
        // Saving component references to improve performance.
        _Transform = GetComponent<Transform>();
        _Controller = GetComponent<CharacterController>();
 
        // Setting initial values.
        _Speed = _WalkSpeed;
        _RayDistance = _Controller.height * .5f + _Controller.radius;
        _SlideLimit = _Controller.slopeLimit - .1f;
        _JumpTimer = _AntiBunnyHopFactor;
    }
 
 
    private void Update()
    {
        // If the run button is set to toggle, then switch between walk/run speed. (We use Update for this...
        // FixedUpdate is a poor place to use GetButtonDown, since it doesn't necessarily run every frame and can miss the event)
        
        float inputX = Input.GetAxis("Horizontal");
        float inputY = Input.GetAxis("Vertical");
        
        if (_ToggleRun && _Grounded && Input.GetButtonDown("Run"))
        {
            _Speed = (_Speed == _WalkSpeed ? _RunSpeed : _WalkSpeed);
        }

        // If both horizontal and vertical are used simultaneously, limit speed (if allowed), so the total doesn't exceed normal move speed
        float inputModifyFactor = (inputX != 0.0f && inputY != 0.0f && _LimitDiagonalSpeed) ? .7071f : 1.0f;
 
        if (_Grounded)
        {
            bool sliding = false;
            // See if surface immediately below should be slid down. We use this normally rather than a ControllerColliderHit point,
            // because that interferes with step climbing amongst other annoyances
            if (Physics.Raycast(_Transform.position, -Vector3.up, out _Hit, _RayDistance))
            {
                if (Vector3.Angle(_Hit.normal, Vector3.up) > _SlideLimit)
                {
                    sliding = true;
                }
            }
            // However, just raycasting straight down from the center can fail when on steep slopes
            // So if the above raycast didn't catch anything, raycast down from the stored ControllerColliderHit point instead
            else
            {
                Physics.Raycast(_ContactPoint + Vector3.up, -Vector3.up, out _Hit);
                if (Vector3.Angle(_Hit.normal, Vector3.up) > _SlideLimit)
                {
                    sliding = true;
                }
            }
 
            // If we were falling, and we fell a vertical distance greater than the threshold, run a falling damage routine
            if (_Falling)
            {
                _Falling = false;
                if (_Transform.position.y < _FallStartLevel - _FallingThreshold)
                {
                    OnFell(_FallStartLevel - _Transform.position.y);
                }
            }
 
            // If running isn't on a toggle, then use the appropriate speed depending on whether the run button is down
            if (!_ToggleRun)
            {
                _Speed = Input.GetKey(KeyCode.LeftShift) ? _RunSpeed : _WalkSpeed;
            }
 
            // If sliding (and it's allowed), or if we're on an object tagged "Slide", get a vector pointing down the slope we're on
            if ((sliding && _SlideWhenOverSlopeLimit) || (_SlideOnTaggedObjects && _Hit.collider.tag == "Slide"))
            {
                Vector3 hitNormal = _Hit.normal;
                _MoveDirection = new Vector3(hitNormal.x, -hitNormal.y, hitNormal.z);
                Vector3.OrthoNormalize(ref hitNormal, ref _MoveDirection);
                _MoveDirection *= _SlideSpeed;
                _PlayerControl = false;
            }
            // Otherwise recalculate moveDirection directly from axes, adding a bit of -y to avoid bumping down inclines
            else
            {
                _MoveDirection = new Vector3(inputX * inputModifyFactor, -_AntiBumpFactor, inputY * inputModifyFactor);
                _MoveDirection = _Transform.TransformDirection(_MoveDirection) * _Speed;
                _PlayerControl = true;
            }
 
            // Jump! But only if the jump button has been released and player has been grounded for a given number of frames
            if (!Input.GetButton("Jump"))
            {
                _JumpTimer++;
            }
            else if (_JumpTimer >= _AntiBunnyHopFactor)
            {
                _MoveDirection.y = _JumpSpeed;
                _JumpTimer = 0;
            }
        }
        else
        {
            // If we stepped over a cliff or something, set the height at which we started falling
            if (!_Falling)
            {
                _Falling = true;
                _FallStartLevel = _Transform.position.y;
            }
 
            // If air control is allowed, check movement but don't touch the y component
            if (_AirControl && _PlayerControl)
            {
                _MoveDirection.x = inputX * _Speed * inputModifyFactor;
                _MoveDirection.z = inputY * _Speed * inputModifyFactor;
                _MoveDirection = _Transform.TransformDirection(_MoveDirection);
            }
        }
 
        // Apply gravity
        _MoveDirection.y -= _Gravity * Time.deltaTime;
 
        // Move the controller, and set grounded true or false depending on whether we're standing on something
        _Grounded = (_Controller.Move(_MoveDirection * Time.deltaTime) & CollisionFlags.Below) != 0;
    }
 
 
    // Store point that we're in contact with for use in FixedUpdate if needed
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        _ContactPoint = hit.point;
    }
 
 
    // This is the place to apply things like fall damage. You can give the player hitpoints and remove some
    // of them based on the distance fallen, play sound effects, etc.
    private void OnFell(float fallDistance)
    {
        print("Ouch! Fell " + fallDistance + " units!");
    }
}