using System.Collections.Generic;
using System.Linq;
using Cancel.Interpolation;
using Cancel.RateLimit;
using UnityEngine;
using System;
using Random = UnityEngine.Random;

public class PlayerShootingScript : uLink.MonoBehaviour
{
    public const float AimingTime = 0.75f;

	public int BurstCount = 8;
	public float ShotCooldown = 0.045f;
	public float ReloadTime = 0.45f;
    public float BurstSpread = 1.5f;
    public float ShotgunSpreadBase = 0.375f;
    public float ShotgunSpread = 10;
    public float ShotgunBulletSpeedMultiplier = 0.25f;
    public float ShotgunHomingSpeed = 0.675f;
    public float CannonChargeTime = 0.5f;
    public float HeatAccuracyFudge = 0.5f;

    // Amount to modify heat by when when a shot is fired and we're zoomed in
	// (instead of at regular zoom) (we want accuracy to decrease more slowly
	// when zoomed in).
    public float HeatAccuracyZoomedMultiplier = 0.525f;

    // Some fudge amount that modifies the amount of the heat that will be added
	// if a shot is fired right now.
    public float CurrentHeatAccuracyFudge
    {
        get
        {
            if (playerCamera != null)
            {
                return HeatAccuracyFudge * (playerCamera.IsZoomedIn ? HeatAccuracyZoomedMultiplier : 1.0f);
            }
            return HeatAccuracyFudge;
        }
    }

    // How much to multiply the shot cooldown when we're zoomed (presumably, to
	// slow the firing rate when zoomed).
    public float ShotCooldownZoomedMultiplier = 2.35f;
	
    public AudioSource reloadSound;
    public AudioSource targetSound;
    public AudioSource pepperGunSound;
    public AudioSource burstGunSound;
    public AudioSource FireRailSound;

    public BulletScript bulletPrefab;
    public BulletScript fastBulletPrefab;
    public BulletScript railPrefab;
    public BulletScript railCosmeticPrefab;
    public BulletScript cannonBulletPrefab;
    public Transform gun;

    public Texture2D cannonIndicator;
    public AnimationCurve cannonOuterScale;
    public AnimationCurve cannonInnerScale;

    // Recoil & reloading animations and stuff
    private ScalarSpring GunRecoilSpring;
    private ScalarSpring ReloaderSpring;
    private Throttler<float> GunRecoilThrotter;
    private Delayer<float> ReloaderDelayer; 
    public Transform BarrelTransform;
    public Transform ReloaderTransform;

	public float heat = 0.0f;
	float cooldownLeft = 0.0f;
    int bulletsLeft;

    // The amount that will get added to the shot cooldown if a shot is fired
	// right now. Modulated by whether or not we're zoomed in.
    private float CurrentShotCooldown
    {
        get
        {
            if (playerCamera != null)
            {
                return ShotCooldown * (playerCamera.IsZoomedIn ? ShotCooldownZoomedMultiplier : 1.0f);
            }
            return ShotCooldown;
        }
    }

    public delegate void ShotFiredHandler();
    // Invoked when a shot is fired (either primary or secondary)
    public event ShotFiredHandler OnShotFired = delegate {};
    // Invoked when a shotgun blast is fired
    public event ShotFiredHandler OnShotgunFired = delegate {};

    public float GunRotationSmoothingSpeed = 7.0f;

    //float cannonChargeCountdown = CannonChargeTime;
    WeaponIndicatorScript weaponIndicator;
    // public because HealthScript accesses it :x
    public List<WeaponIndicatorScript.PlayerData> targets;

    CameraScript playerCamera;
    PlayerScript playerScript;

    private Vector3 firingDirection;

    public void Awake()
    {
		bulletsLeft = BurstCount;
        playerCamera = GetComponentInChildren<CameraScript>();
        weaponIndicator = Camera.main.GetComponent<WeaponIndicatorScript>();
        targets = weaponIndicator.Targets;
        playerScript = GetComponent<PlayerScript>();

        GunRecoilThrotter = new Throttler<float>();
        GunRecoilThrotter.MinimumTimeBetweenItems = 0.09f;
        GunRecoilSpring = new ScalarSpring(0f);
        GunRecoilSpring.Strength = 2000f;
        GunRecoilSpring.Damping = 0.00000000001f;
        ReloaderSpring = new ScalarSpring(0f);
        ReloaderSpring.Strength = 200f;
        ReloaderSpring.Damping = 0.00000001f;
        ReloaderDelayer = new Delayer<float>();
        ReloaderDelayer.DelayTime = 0.25f;
    }

    public void Start()
    {
        cooldownLeft += 1f;
    }

    public bool CharacterIsInTargets(PlayerScript otherCharacter)
    {
        for (int i = 0; i < targets.Count; i++)
            if (targets[i].Script == otherCharacter) return true;
        return false;
    }

    // Usually 0.5 ~ 1.5
    private void AddGunImpulse(float strength)
    {
        const int maxRecoilItems = 1;
        if (GunRecoilThrotter.Items.Count < maxRecoilItems)
            GunRecoilThrotter.Add(-strength * 1000);
        //GunRecoilSpring.AddImpulse(-strength * 1000);
    }

    private void AddReloadImpulse()
    {
        ReloaderDelayer.Add(500);
        //ReloaderSpring.AddImpulse(500);
    }

    /*void Update()
    {
        gun.LookAt(playerCamera.GetTargetPosition());

        if (playerScript.Paused)
            bulletsLeft = BurstCount;
    }*/
	
    // TODO unused, looks like it should go below in the homing firing
    //WeaponIndicatorScript.PlayerData GetFirstTarget()
    //{
    //    var aimedAt = targets.Where(x => x.SinceInCrosshair >= AimingTime );
    //    return aimedAt.OrderBy( x => Guid.NewGuid() ).First();
    //}

    public void Update()
    {
        var actualTargetPosition = playerCamera.GetTargetPosition();
        firingDirection = (actualTargetPosition - gun.transform.position).normalized;
        var gunRotationAngles = Quaternion.FromToRotation(Vector3.forward, firingDirection).eulerAngles;
        var desiredGunRotation = Quaternion.Euler(gunRotationAngles.x, gunRotationAngles.y, 0);
        gun.transform.rotation = Quaternion.Slerp(gun.transform.rotation, desiredGunRotation,
            1.0f - Mathf.Pow(GunRotationSmoothingSpeed, -GunRotationSmoothingSpeed * Time.deltaTime));

        // Update local position of barrel from recoil
        foreach (var impulse in GunRecoilThrotter.Update())
        {
            GunRecoilSpring.AddImpulse(impulse);
        }
        GunRecoilSpring.Update();
        Vector3 barrelLocalPosition = BarrelTransform.localPosition;
        barrelLocalPosition.z = GunRecoilSpring.CurrentValue;
        BarrelTransform.localPosition = barrelLocalPosition;

        // Reload spring
        foreach (var impulse in ReloaderDelayer.Update())
            ReloaderSpring.AddImpulse(impulse);
        ReloaderSpring.Update();
        Vector3 reloadLocalPosition = ReloaderTransform.localPosition;
        reloadLocalPosition.z = ReloaderSpring.CurrentValue;
        ReloaderTransform.localPosition = reloadLocalPosition;

        if (playerScript.Paused)
            bulletsLeft = BurstCount;
		
        if ( networkView.isMine && Screen.lockCursor && !playerScript.Paused )
		{
			cooldownLeft = Mathf.Max( 0, cooldownLeft - Time.deltaTime );
			heat = Mathf.Clamp01( heat - Time.deltaTime );
            weaponIndicator.CooldownStep = 1 - Math.Min( Math.Max(cooldownLeft - ShotCooldown, 0) / ReloadTime, 1 );

		    if( cooldownLeft <= Mathf.Epsilon )
            {
                // Shotgun
                if( Input.GetButton( "Alternate Fire") )
                {
                    OnShotFired();
                    OnShotgunFired();

                    // Rail shot
                    if (bulletsLeft == BurstCount && playerCamera.IsZoomedIn)
                    {
                        DoRailShot();
                        bulletsLeft = 0;
                        cooldownLeft += ReloadTime * 2.7f;

                        var recoilImpulse = -playerCamera.LookingDirection * 2.35f;
                        recoilImpulse *= playerScript.controller.isGrounded ? 25 : 87.5f;
                        recoilImpulse.y *= playerScript.controller.isGrounded ? 0.1f : 0.375f;
                        playerScript.AddRecoil(recoilImpulse);
                    }

                    // Homing/shotgun
                    else
                    {
                        // find homing target(s)
    					var aimedAt = targets.Where( x => x.SinceInCrosshair >= AimingTime ).ToArray();

    					var bulletsShot = bulletsLeft;
                        var first = true;
                        int homingFired = 0;
                        while( bulletsLeft > 0 )
                        {
                            if( !aimedAt.Any() )
                                DoHomingShot( ShotgunSpread, null, 0, first );
                            else
    						{
            					var pd = aimedAt.OrderBy( x => Guid.NewGuid() ).First();
                                DoHomingShot( ShotgunSpread, pd.Script, Mathf.Clamp01( pd.SinceInCrosshair / AimingTime ) * ShotgunHomingSpeed, first );
    						    homingFired++;
    						}
    						
                            cooldownLeft += ShotCooldown;
                            first = false;
                        }
                        cooldownLeft += ReloadTime;

                        float lockOnReduction = (float)homingFired / (float)BurstCount;
                        float amountOfLockToRemove = WeaponIndicatorScript.PlayerData.LockStrengthLimitMultiplier *
                                                     lockOnReduction;
                        for (int i = 0; i < targets.Count; i++)
                        {
                            targets[i].LockStrength -= amountOfLockToRemove;
                            targets[i].ClampStrength();
                        }

                        var recoilImpulse = -gun.forward * ((float)bulletsShot / BurstCount);
                        recoilImpulse *= playerScript.controller.isGrounded ? 25 : 87.5f;
                        recoilImpulse.y *= playerScript.controller.isGrounded ? 0.1f : 0.375f;
                        playerScript.AddRecoil(recoilImpulse);

                        float cosmeticAmount = (float)bulletsShot / (float)BurstCount;
                        playerCamera.AddGunShotImpulse(Mathf.Lerp(0.0f, 2.0f, cosmeticAmount));
                        AddGunImpulse(cosmeticAmount);
                    }

                    //cannonChargeCountdown = CannonChargeTime;
                }

                // Burst
                else if (Input.GetButton("Fire")) // burst fire
                {
                    //OnShotFired();

                    DoShot( BurstSpread );
                    cooldownLeft += CurrentShotCooldown;
                    if( bulletsLeft <= 0 )
                        cooldownLeft += ReloadTime;
                }

                if (bulletsLeft != BurstCount && Input.GetButton("Reload"))
                {
                    bulletsLeft = BurstCount;
					
					if( GlobalSoundsScript.soundEnabled )
                    	reloadSound.Play();
					
                    AddReloadImpulse();
                    cooldownLeft += ReloadTime;
                }
            }

            if( bulletsLeft <= 0 ) 
            {
                bulletsLeft = BurstCount;
				if( GlobalSoundsScript.soundEnabled )
                	reloadSound.Play();
                AddReloadImpulse();
            }

			
            TestScreenSpaceLockTargets();
            CheckTargets();

            // Update weapon indicator bullets count. Kind of hacky because
            // bullet availability is not tracked like you might expect it to
			// be.
		    weaponIndicator.BulletCapacity = BurstCount;
		    if (bulletsLeft == BurstCount && cooldownLeft >= Mathf.Epsilon)
		    {
		        weaponIndicator.BulletsAvailable = 0;
		    }
		    else if (cooldownLeft > Mathf.Epsilon && weaponIndicator.BulletsAvailable < bulletsLeft)
		    {
                // Do nothing, probably reloading
		    }
		    else
		    {
    		    weaponIndicator.BulletsAvailable = bulletsLeft;
		    }
		}
    }

    private void TestScreenSpaceLockTargets()
    {
        var screenCenter = new Vector2(Screen.width / 2f, Screen.height / 2f);
        var allowedDistance = 130 * Screen.height / 1500f;

        // Test for players in crosshair
        foreach (var ps in PlayerScript.UnsafeAllEnabledPlayerScripts)
        {
            if( ps == GetComponent<PlayerScript>() ) // Is targeting self?
                continue;

            var health = ps.gameObject.GetComponent<HealthScript>();
            var position = ps.transform.position;
            var screenPos = Camera.main.WorldToScreenPoint(position);

            if (health.Health > 0 && screenPos.z > 0 && ( screenPos.XY() - screenCenter ).magnitude < allowedDistance)
            {
                WeaponIndicatorScript.PlayerData data;
                if ( (data = targets.FirstOrDefault( x => x.Script == ps ) ) == null )
                    targets.Add( data = new WeaponIndicatorScript.PlayerData { Script = ps, WasLocked = false } );

                data.TimeSinceLastLockSend += Time.deltaTime;

                // Only lock on half as fast when through walls
                bool canSee = playerScript.CanSeeOtherPlayer(ps);
                float strengthAdjustment = canSee ? 1.0f : 0.5f;

                // TODO lots of redundant stuff here
                data.ScreenPosition = screenPos.XY();
                data.LockStrength += Time.deltaTime * strengthAdjustment;
                data.ClampStrength();
                data.Found = true;
				
                if ( !data.WasLocked && data.Locked )
                {	
                    if( GlobalSoundsScript.soundEnabled )
                        targetSound.Play();

                }
            }
            else
            {
                WeaponIndicatorScript.PlayerData data = targets.Find(data2 => data2.Script == ps);
                if (data != null)
                {
                    data.ScreenPosition = screenPos.XY();
                    data.Found = false;
                    data.LockStrength -= Time.deltaTime * WeaponIndicatorScript.PlayerData.LockLossTimeMultiplier;
                    data.ClampStrength();
                    data.TimeSinceLastLockSend += Time.deltaTime;
                }
            }
        }
    }
	
	private void CheckTargets()
	{
        for( int i = targets.Count - 1; i >= 0; i-- )
        {
            if( targets[i].Script != null )
            {
                ScreenSpaceDebug.AddMessageOnce(targets[i].LockStrength.ToString(), targets[i].Script.transform.position);
                if (targets[i].NeedsLockSent)
                {
                    targets[i].Script.GetTargetedBy(playerScript);
                    targets[i].TimeSinceLastLockSend = 0f;
                }
                if (targets[i].WasLocked && !targets[i].Locked)
                {
                    targets[i].Script.GetUntargetedBy(playerScript);
                }
                if (targets[i].LockStrength <= Mathf.Epsilon)
                    targets.RemoveAt(i);
                else
                {
                    targets[i].WasLocked = targets[i].Locked;
                }
            }
            else 
            {
                targets.RemoveAt( i );
            }
        }
	}
	
    void DoShot(float spread)
    {
        bulletsLeft -= 1;
        spread += heat * CurrentHeatAccuracyFudge;
		heat += 0.25f;

        float roll = Random.value * 360;
        Quaternion spreadRotation =
            Quaternion.Euler( 0, 0, roll ) *
            Quaternion.Euler( Random.value * spread, 0, 0 ) *
            Quaternion.Euler( 0, 0, -roll );
        Quaternion firingRotation = Quaternion.FromToRotation(Vector3.forward, firingDirection);

        Vector3 finalFiringPosition = gun.position + firingDirection*4.0f;
        Quaternion finalFiringRotation = firingRotation*spreadRotation;
        if (playerCamera.IsZoomedIn)
        {
            if (playerScript.ShouldSendMessages)
            {
                networkView.UnreliableRPC("ShootFast", uLink.RPCMode.Others,
                    finalFiringPosition, finalFiringRotation, uLink.Network.player );
            }
            ShootFast( finalFiringPosition, finalFiringRotation, uLink.Network.player );
            AddGunImpulse(0.5f);
        }
        else
        {
            if (playerScript.ShouldSendMessages)
            {
                networkView.UnreliableRPC("Shoot", uLink.RPCMode.Others,
                    finalFiringPosition, finalFiringRotation, uLink.Network.player);
            }
            Shoot( finalFiringPosition, finalFiringRotation, uLink.Network.player );
            AddGunImpulse(0.3f);
        }
        playerCamera.AddGunShotImpulse(0.65f);
    }

    public void InstantReload()
    {
        bulletsLeft = BurstCount;
    }

    void DoHomingShot(float spread, PlayerScript target, float homing, bool doSound)
    {
        bulletsLeft -= 1;

        spread *= ( ShotgunSpreadBase + homing * 5 );

        float roll = RandomHelper.Between(homing * 90, 360 - homing * 90);
        Quaternion spreadRotation =
            Quaternion.Euler(0, 0, roll) *
            Quaternion.Euler(Random.value * spread, 0, 0) *
            Quaternion.Euler(0, 0, -roll);
        Quaternion firingRotation = Quaternion.FromToRotation(Vector3.forward, firingDirection);

        var lastKnownPosition = Vector3.zero;
        uLink.NetworkPlayer targetOwner = uLink.Network.player;
        if( target != null )
        {
            targetOwner = target.networkView.owner;
            lastKnownPosition = target.transform.position;
        }

        if (playerScript.ShouldSendMessages)
        {
            networkView.UnreliableRPC("ShootHoming", uLink.RPCMode.Others,
                gun.position + firingDirection*4.0f, firingRotation*spreadRotation,
                uLink.Network.player, targetOwner, lastKnownPosition, homing, doSound);
        }
        ShootHoming(
            gun.position + firingDirection * 4.0f, firingRotation * spreadRotation,
            uLink.Network.player, targetOwner, lastKnownPosition, homing, doSound );
    }

    void DoRailShot()
    {
        Vector3 finalFiringPosition = gun.position + firingDirection*4.0f;
        Quaternion finalFiringRotation = Quaternion.FromToRotation(Vector3.forward, firingDirection);
        if (playerScript.ShouldSendMessages)
        {
            networkView.UnreliableRPC("ShootRail", uLink.RPCMode.Others,
                finalFiringPosition, finalFiringRotation, uLink.Network.player);
        }
        ShootRail( finalFiringPosition, finalFiringRotation, uLink.Network.player );
        playerCamera.AddGunShotImpulse(1.7f);
        AddGunImpulse(3.0f);
    }

    [RPC]
    protected void Shoot(Vector3 position, Quaternion rotation, uLink.NetworkPlayer player)
    {
        BulletScript bullet = (BulletScript)Instantiate( bulletPrefab, position, rotation );
        bullet.Instigator = playerScript.Possessor;
		
		if( GlobalSoundsScript.soundEnabled )
        	burstGunSound.Play();
    }
    [RPC]
    protected void ShootFast(Vector3 position, Quaternion rotation, uLink.NetworkPlayer player)
    {
        BulletScript bullet = (BulletScript)Instantiate( fastBulletPrefab, position, rotation );
        bullet.Instigator = playerScript.Possessor;
		
		if( GlobalSoundsScript.soundEnabled )
        	burstGunSound.Play();
    }

    // TODO very inefficient
    [RPC]
    protected void ShootHoming(Vector3 position, Quaternion rotation, uLink.NetworkPlayer player, uLink.NetworkPlayer target, Vector3 lastKnownPosition, float homing, bool doSound)
    {
        BulletScript bullet = (BulletScript)Instantiate( bulletPrefab, position, rotation );
        bullet.Instigator = playerScript.Possessor;

		PlayerScript targetScript;
        try
        {
            targetScript = PlayerScript.UnsafeAllEnabledPlayerScripts.Where(
                x => x.networkView.owner == target).OrderBy(x => Vector3.Distance(x.transform.position, lastKnownPosition)).FirstOrDefault();
        }
        catch (Exception) { targetScript = null; }

        bullet.target = targetScript == null ? null : targetScript.transform;
        bullet.homing = homing;
        bullet.speed *= ShotgunBulletSpeedMultiplier;
        bullet.recoil = 1;

        if( doSound )
			if( GlobalSoundsScript.soundEnabled )
				pepperGunSound.Play();
    }

    [RPC]
    protected void ShootRail(Vector3 position, Quaternion rotation, uLink.NetworkPlayer player)
    {
        BulletScript bullet = (BulletScript)Instantiate( railPrefab, position, rotation );
        BulletScript cosmeticBullet = (BulletScript)Instantiate( railCosmeticPrefab, position, rotation );
        bullet.Instigator = playerScript.Possessor;
        cosmeticBullet.Instigator = playerScript.Possessor;
		
		if( GlobalSoundsScript.soundEnabled )
        	FireRailSound.Play();
    }
}
