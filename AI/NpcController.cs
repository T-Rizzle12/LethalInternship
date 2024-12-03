﻿using GameNetcodeStuff;
using LethalInternship.Constants;
using LethalInternship.Enums;
using LethalInternship.Managers;
using LethalInternship.Patches.NpcPatches;
using LethalInternship.Utils;
using ModelReplacement;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace LethalInternship.AI
{
    internal class NpcController
    {
        public PlayerControllerB Npc { get; set; } = null!;

        public List<PlayerPhysicsRegion> CurrentInternPhysicsRegions = new List<PlayerPhysicsRegion>();

        public bool HasToMove { get { return lastMoveVector.y > 0f; } }
        public bool IsControllerInCruiser;

        // Public variables to pass to patch
        public bool IsCameraDisabled;
        public bool IsJumping;
        public bool IsFallingFromJump;
        public float CrouchMeter;
        public bool IsWalking;
        public float PlayerSlidingTimer;
        public bool DisabledJetpackControlsThisFrame;
        public bool StartedJetpackControls;
        public Vector3 RightArmProceduralTargetBasePosition;
        public float TimeSinceTakingGravityDamage;
        public bool TeleportingThisFrame;
        public float PreviousFrameDeltaTime;
        public float CameraUp;

        //Audio
        public OccludeAudio OccludeAudioComponent = null!;
        public AudioLowPassFilter AudioLowPassFilterComponent = null!;
        public AudioHighPassFilter AudioHighPassFilterComponent = null!;

        public Vector3 MoveVector;
        public bool UpdatePositionForNewlyJoinedClient;
        public bool GrabbedObjectValidated;
        public float UpdatePlayerLookInterval;
        public int PlayerMask;
        public bool IsTouchingGround;
        public RaycastHit GroundHit;
        public EnemyAI? EnemyInAnimationWith;
        public bool ShouldAnimate;
        public Vector3 NearEntitiesPushVector;

        private InternAI InternAIController
        {
            get
            {
                if (_internAIController == null)
                {
                    _internAIController = InternManager.Instance.GetInternAI((int)Npc.playerClientId);
                    if (_internAIController == null)
                    {
                        throw new NullReferenceException($"{PluginInfo.PLUGIN_GUID} v{PluginInfo.PLUGIN_VERSION}: error no internAI attached to NpcController playerClientId {Npc.playerClientId}.");
                    }
                }
                return _internAIController;
            }
        }
        private InternAI? _internAIController;

        private int movementHinderedPrev;
        private float sprintMultiplier = 1f;
        private float slopeModifier;
        private float limpMultiplier = 0.6f;
        private Vector3 walkForce;
        private bool isFallingNoJump;

        private Dictionary<string, bool> dictAnimationBoolPerItem = null!;

        private float upperBodyAnimationsWeight;
        private float exhaustionEffectLerp;
        private bool disabledJetpackControlsThisFrame;

        private bool wasUnderwaterLastFrame;
        private float drowningTimer = 1f;
        private bool setFaceUnderwater;
        private float syncUnderwaterInterval;

        private EnumObjectsLookingAt enumObjectsLookingAt;

        private int oldSentIntEnumObjectsLookingAt;
        private Vector3 oldSentDirectionToUpdateTurnBodyTowardsTo;
        private Vector3 oldSentPositionPlayerEyeToLookAt;
        private Vector3 oldSentPositionToLookAt;

        private Vector3 directionToUpdateTurnBodyTowardsTo;
        private Vector3 positionPlayerEyeToLookAt;
        private Vector3 positionToLookAt;

        private Vector2 lastMoveVector;
        private float floatSprint;
        private bool goDownLadder;

        private int[] animationHashLayers = null!;
        private List<int> currentAnimationStateHash = null!;
        private List<int> previousAnimationStateHash = null!;
        private float updatePlayerAnimationsInterval;
        private float currentAnimationSpeed;
        private float previousAnimationSpeed;

        private float timerShowName;
        private float timerPlayFootstep;

        public NpcController(PlayerControllerB npc)
        {
            this.Npc = npc;
            Init();
        }

        /// <summary>
        /// Initialize the <c>PlayerControllerB</c>
        /// </summary>
        public void Awake()
        {
            Plugin.LogDebug("Awake intern controller.");
            Init();

            PatchesUtil.FieldInfoPreviousAnimationStateHash.SetValue(Npc, new List<int>(new int[Npc.playerBodyAnimator.layerCount]));
            PatchesUtil.FieldInfoCurrentAnimationStateHash.SetValue(Npc, new List<int>(new int[Npc.playerBodyAnimator.layerCount]));
        }

        private void Init()
        {
            Npc.isHostPlayerObject = false;
            Npc.serverPlayerPosition = Npc.transform.position;
            Npc.gameplayCamera.enabled = false;
            Npc.visorCamera.enabled = false;
            Npc.thisPlayerModel.enabled = true;
            Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
            Npc.thisPlayerModelArms.enabled = false;

            this.IsCameraDisabled = true;
            Npc.sprintMeter = 1f;
            Npc.ItemSlots = new GrabbableObject[1];
            RightArmProceduralTargetBasePosition = Npc.rightArmProceduralTarget.localPosition;

            Npc.usernameBillboardText.text = Npc.playerUsername;
            Npc.usernameAlpha.alpha = 1f;
            Npc.usernameCanvas.gameObject.SetActive(true);
            Npc.usernameCanvas.transform.position = Npc.usernameCanvas.transform.position + new Vector3(0, 0.1f, 0);

            Npc.previousElevatorPosition = Npc.playersManager.elevatorTransform.position;
            if (Npc.gameObject.GetComponent<Rigidbody>())
            {
                Npc.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
            }
            Npc.gameObject.GetComponent<CharacterController>().enabled = false;

            Npc.playerBodyAnimator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            foreach (var skinnedMeshRenderer in Npc.gameObject.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                skinnedMeshRenderer.updateWhenOffscreen = false;
            }

            animationHashLayers = new int[Npc.playerBodyAnimator.layerCount];
            currentAnimationStateHash = new List<int>(new int[Npc.playerBodyAnimator.layerCount]);
            previousAnimationStateHash = new List<int>(new int[Npc.playerBodyAnimator.layerCount]);
        }

        /// <summary>
        /// Update called from <see cref="PlayerControllerBPatch.Update_PreFix"><c>PlayerControllerBPatch.Update_PreFix</c></see> 
        /// instead of the real update from <c>PlayerControllerB</c>.
        /// </summary>
        /// <remarks>
        /// Update the move vector in regard of the field set with the order methods,<br/>
        /// update the movement of the <c>PlayerControllerB</c> against various hazards,<br/>
        /// while sinking, drowning, jumping, falling, in jetpack, in special interaction with enemies.<br/>
        /// Sync the rotation with other clients.
        /// </remarks>
        public void Update()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            // The owner of the intern (and the controller)
            // updates and moves the controller
            if (InternAIController.IsClientOwnerOfIntern() && Npc.isPlayerControlled)
            {
                // Updates the state of the CharacterController and the animator controller
                UpdateOwnerChanged(true);

                Npc.rightArmProceduralRig.weight = Mathf.Lerp(Npc.rightArmProceduralRig.weight, 0f, 25f * Time.deltaTime);

                // Set the move input vector for moving the controller
                UpdateMoveInputVectorForOwner();

                // Force turn if needed
                ForceTurnTowardsTarget();

                // Turn the body towards the direction set beforehand
                UpdateTurnBodyTowardsDirection();

                // Manage the drowning state of the intern
                SetFaceUnderwaterFilters();

                // Update the animation of walking under numerous conditions
                UpdateWalkingStateForOwner();

                // Sync with clients if the intern is performing emote
                UpdateEmoteStateForOwner();

                // Update and sync with clients, if the intern is sinking or not and should die or not
                UpdateSinkingStateForOwner();

                // Update the center and the height of the <c>CharacterController</c>
                UpdateCenterAndHeightForOwner();

                // Update the rotation of the controller when using jetpack controls
                UpdateJetPackControlsForOwner();

                if (!Npc.inSpecialInteractAnimation || Npc.inShockingMinigame || instanceSOR.suckingPlayersOutOfShip)
                {
                    // Move the body of intern
                    UpdateMoveControllerForOwner();

                    // Check if the intern is falling and update values accordingly
                    UpdateFallValuesForOwner();

                    Npc.externalForces = Vector3.zero;
                    if (!TeleportingThisFrame && Npc.teleportedLastFrame)
                    {
                        Npc.teleportedLastFrame = false;
                    }

                    // Update movement when using jetpack controls
                    UpdateJetPackMoveValuesForOwner();
                }
                else if (Npc.isClimbingLadder)
                {
                    // Update movement when using ladder
                    UpdateMoveWhenClimbingLadder();
                }
                TeleportingThisFrame = false;

                // Rotations
                this.UpdateLookAt();

                Npc.playerEye.position = Npc.gameplayCamera.transform.position;
                Npc.playerEye.rotation = Npc.gameplayCamera.transform.rotation;

                // Update UpdatePlayerLookInterval
                if (NetworkManager.Singleton != null && Npc.playersManager.connectedPlayersAmount > 0)
                {
                    this.UpdatePlayerLookInterval += Time.deltaTime;
                }

                // Update animations
                UpdateAnimationsForOwner();
            }
            else // If not owner, the client just update the position and rotation of the controller
            {
                // Updates the state of the CharacterController and the animator controller
                UpdateOwnerChanged(false);

                // Sync position and rotations
                UpdateSyncPositionAndRotationForNotOwner();

                // Update animations
                UpdateInternAnimationsLocalForNotOwner(animationHashLayers);
            }

            Npc.timeSincePlayerMoving += Time.deltaTime;
            Npc.timeSinceMakingLoudNoise += Time.deltaTime;

            // Update the localarms and rotation when in special interact animation
            UpdateInSpecialInteractAnimationEffect();

            // Update animation layer when using emotes
            UpdateEmoteEffects();

            // Update the sinking values and effect
            UpdateSinkingEffects();

            // Update the active audio reverb filter
            UpdateActiveAudioReverbFilter();

            // Update animations when holding items and exhausion
            UpdateAnimationUpperBody();

            PlayFootstepIfCloseNoAnimation();
        }

        /// <summary>
        /// Updates the state of the <c>CharacterController</c>
        /// and update the animator controller with its animation
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateOwnerChanged(bool isOwner)
        {
            if (isOwner)
            {
                if (IsCameraDisabled)
                {
                    IsCameraDisabled = false;
                    Npc.gameplayCamera.enabled = false;
                    Npc.visorCamera.enabled = false;
                    Npc.thisPlayerModelArms.enabled = false;
                    Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    Npc.mapRadarDirectionIndicator.enabled = false;
                    Npc.thisController.enabled = false;
                    UpdateRuntimeAnimatorController(isOwner);
                }
            }
            else
            {
                if (!this.IsCameraDisabled)
                {
                    this.IsCameraDisabled = true;
                    Npc.gameplayCamera.enabled = false;
                    Npc.visorCamera.enabled = false;
                    Npc.thisPlayerModel.shadowCastingMode = ShadowCastingMode.On;
                    Npc.thisPlayerModelArms.enabled = false;
                    Npc.mapRadarDirectionIndicator.enabled = false;
                    UpdateRuntimeAnimatorController(isOwner);
                    Npc.thisController.enabled = false;
                    if (Npc.gameObject.GetComponent<Rigidbody>())
                    {
                        Npc.gameObject.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.None;
                    }
                }
            }
        }

        /// <summary>
        /// Updates the animator controller if the owner of intern has changed
        /// </summary>
        /// <param name="isOwner"></param>
        private void UpdateRuntimeAnimatorController(bool isOwner)
        {
            // Save animations states
            AnimatorStateInfo[] layerInfo = new AnimatorStateInfo[Npc.playerBodyAnimator.layerCount];
            for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                layerInfo[i] = Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i);
            }

            // Change runtimeAnimatorController
            if (isOwner)
            {
                if (Npc.playerBodyAnimator.runtimeAnimatorController != Npc.playersManager.localClientAnimatorController)
                {
                    Npc.playerBodyAnimator.runtimeAnimatorController = Npc.playersManager.localClientAnimatorController;
                }
            }
            else
            {
                if (Npc.playerBodyAnimator.runtimeAnimatorController != Npc.playersManager.otherClientsAnimatorController)
                {
                    Npc.playerBodyAnimator.runtimeAnimatorController = Npc.playersManager.otherClientsAnimatorController;
                }
            }

            // Push back animations states
            for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                if (Npc.playerBodyAnimator.HasState(i, layerInfo[i].fullPathHash))
                {
                    Npc.playerBodyAnimator.CrossFadeInFixedTime(layerInfo[i].fullPathHash, 0.1f);
                }
            }

            if (dictAnimationBoolPerItem != null)
            {
                foreach (var animationBool in dictAnimationBoolPerItem)
                {
                    Npc.playerBodyAnimator.SetBool(animationBool.Key, animationBool.Value);
                }
            }
        }

        #region Updates npc body for owner

        /// <summary>
        /// Set the move input vector for moving the controller
        /// </summary>
        /// <remarks>
        /// Basically the controller move forward and the rotation is changed in another method if needed (following the AI).
        /// </remarks>
        private void UpdateMoveInputVectorForOwner()
        {
            Npc.moveInputVector.y = this.lastMoveVector.y;
        }

        /// <summary>
        /// Update the animation of walking under numerous conditions
        /// </summary>
        private void UpdateWalkingStateForOwner()
        {
            if (IsWalking)
            {
                if (Npc.moveInputVector.sqrMagnitude <= 0.001
                    || (Npc.inSpecialInteractAnimation && !Npc.isClimbingLadder && !Npc.inShockingMinigame))
                {
                    StopAnimations();
                }
                else if (floatSprint > 0.3f
                            && movementHinderedPrev <= 0
                            && !Npc.criticallyInjured
                            && Npc.sprintMeter > 0.1f)
                {
                    if (!Npc.isSprinting && Npc.sprintMeter < 0.3f)
                    {
                        if (!Npc.isExhausted)
                        {
                            Npc.isExhausted = true;
                        }
                    }
                    else
                    {
                        if (Npc.isCrouching && !Plugin.Config.FollowCrouchWithPlayer)
                        {
                            Npc.Crouch(false);
                        }

                        if (!Npc.isCrouching)
                        {
                            Npc.isSprinting = true;
                        }
                    }
                }
                else
                {
                    Npc.isSprinting = false;
                    if (Npc.sprintMeter < 0.1f)
                    {
                        Npc.isExhausted = true;
                    }
                }

                if (Npc.isSprinting)
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 2.25f, Time.deltaTime * 1f);
                }
                else
                {
                    sprintMultiplier = Mathf.Lerp(sprintMultiplier, 1f, 10f * Time.deltaTime);
                }

                if (Npc.moveInputVector.y < 0.2f && Npc.moveInputVector.y > -0.2f && !Npc.inSpecialInteractAnimation)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, true);
                }
                else
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
                }
                if (Npc.enteringSpecialAnimation)
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                else if (Npc.moveInputVector.y < 0.3f && Npc.moveInputVector.x < 0.3f)
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, -1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                }
                else
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f * Mathf.Clamp(slopeModifier + 1f, 0.7f, 1.4f));
                }
            }
            else
            {
                if (Npc.enteringSpecialAnimation)
                {
                    Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 1f);
                }
                if (Npc.moveInputVector.sqrMagnitude >= 0.001f && (!Npc.inSpecialInteractAnimation || Npc.isClimbingLadder || Npc.inShockingMinigame))
                {
                    IsWalking = true;
                }
            }

            if (Npc.isClimbingLadder)
            {
                Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_ANIMATIONSPEED, 2f);
            }
        }

        /// <summary>
        /// Sync with clients if the intern is performing emote
        /// </summary>
        private void UpdateEmoteStateForOwner()
        {
            if (Npc.performingEmote)
            {
                if (this.Npc.inSpecialInteractAnimation
                    || this.Npc.isPlayerDead
                    || this.Npc.isCrouching
                    || this.Npc.isClimbingLadder
                    || this.Npc.isGrabbingObjectAnimation
                    || this.Npc.inTerminalMenu
                    || this.Npc.isTypingChat)
                {
                    Npc.performingEmote = false;
                    this.InternAIController.SyncStopPerformingEmote();
                }
            }
        }

        /// <summary>
        /// Update and sync with clients, if the intern is sinking or not and should die or not
        /// </summary>
        private void UpdateSinkingStateForOwner()
        {
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_HINDEREDMOVEMENT, Npc.isMovementHindered > 0);
            if (Npc.sourcesCausingSinking == 0)
            {
                if (Npc.isSinking)
                {
                    Npc.isSinking = false;
                    this.InternAIController.SyncChangeSinkingState(false);
                }
            }
            else
            {
                if (Npc.isSinking)
                {
                    Npc.GetCurrentMaterialStandingOn();
                    if (!CheckConditionsForSinkingInQuicksandIntern())
                    {
                        Npc.isSinking = false;
                        this.InternAIController.SyncChangeSinkingState(false);
                    }
                }
                else if (!Npc.isSinking && CheckConditionsForSinkingInQuicksandIntern())
                {
                    Npc.isSinking = true;
                    this.InternAIController.SyncChangeSinkingState(true, Npc.sinkingSpeedMultiplier, Npc.statusEffectAudioIndex);
                }
                if (Npc.sinkingValue >= 1f)
                {
                    Plugin.LogDebug($"SyncKillIntern from sinkingValue for LOCAL client #{Npc.NetworkManager.LocalClientId}, intern object: Intern #{Npc.playerClientId}");
                    this.InternAIController.SyncKillIntern(Vector3.zero, false, CauseOfDeath.Suffocation, 0, default);
                }
                else if (Npc.sinkingValue > 0.5f)
                {
                    Npc.Crouch(false);
                }
            }
        }

        /// <summary>
        /// Update the center and the height of the <c>CharacterController</c>
        /// </summary>
        private void UpdateCenterAndHeightForOwner()
        {
            if (Npc.isCrouching)
            {
                Npc.thisController.center = Vector3.Lerp(Npc.thisController.center, new Vector3(Npc.thisController.center.x, 0.72f, Npc.thisController.center.z), 8f * Time.deltaTime);
                Npc.thisController.height = Mathf.Lerp(Npc.thisController.height, 1.5f, 8f * Time.deltaTime);
            }
            else
            {
                CrouchMeter = Mathf.Max(CrouchMeter - Time.deltaTime * 2f, 0f);
                Npc.thisController.center = Vector3.Lerp(Npc.thisController.center, new Vector3(Npc.thisController.center.x, 1.28f, Npc.thisController.center.z), 8f * Time.deltaTime);
                Npc.thisController.height = Mathf.Lerp(Npc.thisController.height, 2.5f, 8f * Time.deltaTime);
            }
        }

        /// <summary>
        /// Update the rotation of the controller when using jetpack controls
        /// </summary>
        private void UpdateJetPackControlsForOwner()
        {
            if (this.disabledJetpackControlsThisFrame)
            {
                this.disabledJetpackControlsThisFrame = false;
            }
            if (Npc.jetpackControls)
            {
                if (Npc.disablingJetpackControls && IsTouchingGround)
                {
                    this.disabledJetpackControlsThisFrame = true;
                    this.InternAIController.SyncDisableJetpackMode();
                }
                else if (!IsTouchingGround)
                {
                    if (!this.StartedJetpackControls)
                    {
                        this.StartedJetpackControls = true;
                        Npc.jetpackTurnCompass.rotation = Npc.transform.rotation;
                    }
                    Npc.thisController.radius = Mathf.Lerp(Npc.thisController.radius, 1.25f, 10f * Time.deltaTime);
                    Quaternion rotation = Npc.jetpackTurnCompass.rotation;
                    Npc.jetpackTurnCompass.Rotate(new Vector3(0f, 0f, -Npc.moveInputVector.x) * (180f * Time.deltaTime), Space.Self);
                    if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                    {
                        Npc.jetpackTurnCompass.rotation = rotation;
                    }
                    rotation = Npc.jetpackTurnCompass.rotation;
                    Npc.jetpackTurnCompass.Rotate(new Vector3(Npc.moveInputVector.y, 0f, 0f) * (180f * Time.deltaTime), Space.Self);
                    if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                    {
                        Npc.jetpackTurnCompass.rotation = rotation;
                    }
                    if (Npc.jetpackRandomIntensity != -1f)
                    {
                        rotation = Npc.jetpackTurnCompass.rotation;
                        Vector3 a2 = new Vector3(
                            Mathf.Clamp(
                                Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity),
                            -Npc.maxJetpackAngle, Npc.maxJetpackAngle),
                            Mathf.Clamp(
                                Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity), -Npc.maxJetpackAngle, Npc.maxJetpackAngle),
                            Mathf.Clamp(Random.Range(-Npc.jetpackRandomIntensity, Npc.jetpackRandomIntensity), -Npc.maxJetpackAngle, Npc.maxJetpackAngle));
                        Npc.jetpackTurnCompass.Rotate(a2 * Time.deltaTime, Space.Self);
                        if (Npc.maxJetpackAngle != -1f && Vector3.Angle(Npc.jetpackTurnCompass.up, Vector3.up) > Npc.maxJetpackAngle)
                        {
                            Npc.jetpackTurnCompass.rotation = rotation;
                        }
                    }
                    Npc.transform.rotation = Quaternion.Slerp(Npc.transform.rotation, Npc.jetpackTurnCompass.rotation, 8f * Time.deltaTime);
                }
            }
        }

        /// <summary>
        /// Move the body of intern
        /// </summary>
        private void UpdateMoveControllerForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (Npc.isFreeCamera)
            {
                Npc.moveInputVector = Vector2.zero;
            }
            float num3 = Npc.movementSpeed / Npc.carryWeight;
            if (Npc.sinkingValue > 0.73f)
            {
                num3 = 0f;
            }
            else
            {
                if (Npc.isCrouching)
                {
                    num3 /= 1.5f;
                }
                else if (Npc.criticallyInjured && !Npc.isCrouching)
                {
                    num3 *= limpMultiplier;
                }
                if (Npc.isSpeedCheating)
                {
                    num3 *= 15f;
                }
                if (movementHinderedPrev > 0)
                {
                    num3 /= 2f * Npc.hinderedMultiplier;
                }
                if (Npc.drunkness > 0f)
                {
                    num3 *= instanceSOR.drunknessSpeedEffect.Evaluate(Npc.drunkness) / 5f + 1f;
                }
                if (!Npc.isCrouching && CrouchMeter > 1.2f)
                {
                    num3 *= 0.5f;
                }
            }
            if (Npc.isTypingChat || Npc.jetpackControls && !IsTouchingGround || instanceSOR.suckingPlayersOutOfShip)
            {
                Npc.moveInputVector = Vector2.zero;
            }

            float num7;
            if (IsFallingFromJump || isFallingNoJump)
            {
                num7 = 1.33f;
            }
            else if (Npc.drunkness > 0.3f)
            {
                num7 = Mathf.Clamp(Mathf.Abs(Npc.drunkness - 2.25f), 0.3f, 2.5f);
            }
            else if (!Npc.isCrouching && CrouchMeter > 1f)
            {
                num7 = 15f;
            }
            else if (Npc.isSprinting)
            {
                num7 = 5f / (Npc.carryWeight * 1.5f);
            }
            else
            {
                num7 = 10f / Npc.carryWeight;
            }
            walkForce = Vector3.MoveTowards(walkForce, Npc.transform.right * Npc.moveInputVector.x + Npc.transform.forward * Npc.moveInputVector.y, num7 * Time.deltaTime);
            Vector3 vector2 = walkForce * num3 * sprintMultiplier + new Vector3(0f, Npc.fallValue, 0f) + NearEntitiesPushVector;
            vector2 += Npc.externalForces;
            if (Npc.externalForceAutoFade.magnitude > 0.05f)
            {
                vector2 += Npc.externalForceAutoFade;
                Npc.externalForceAutoFade = Vector3.Lerp(Npc.externalForceAutoFade, Vector3.zero, 2f * Time.deltaTime);
            }

            PlayerSlidingTimer = 0f;
            NearEntitiesPushVector = Vector3.zero;

            // Move
            MoveVector = vector2;
        }

        /// <summary>
        /// Check if the intern is falling and update values accordingly
        /// </summary>
        private void UpdateFallValuesForOwner()
        {
            if (Npc.inSpecialInteractAnimation && !Npc.inShockingMinigame)
            {
                return;
            }

            IsTouchingGround = Physics.Raycast(new Ray(Npc.thisPlayerBody.position + Vector3.up * 2, -Vector3.up),
                                               out GroundHit,
                                               2.5f,
                                               StartOfRound.Instance.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore);
            if (!IsTouchingGround)
            {
                if (Npc.jetpackControls && !Npc.disablingJetpackControls)
                {
                    Npc.fallValue = Mathf.MoveTowards(Npc.fallValue, Npc.jetpackCounteractiveForce, 9f * Time.deltaTime);
                    Npc.fallValueUncapped = -8f;
                }
                else
                {
                    Npc.fallValue = Mathf.Clamp(Npc.fallValue - 38f * Time.deltaTime, -150f, Npc.jumpForce);
                    if (Mathf.Abs(Npc.externalForceAutoFade.y) - Mathf.Abs(Npc.fallValue) < 5f)
                    {
                        if (Npc.disablingJetpackControls)
                        {
                            Npc.fallValueUncapped -= 26f * Time.deltaTime;
                        }
                        else
                        {
                            Npc.fallValueUncapped -= 38f * Time.deltaTime;
                        }
                    }
                }
                if (!IsJumping && !IsFallingFromJump)
                {
                    if (!isFallingNoJump)
                    {
                        isFallingNoJump = true;
                        Plugin.LogDebug($"{Npc.playerUsername} isFallingNoJump true");
                        Npc.fallValue = -7f;
                        Npc.fallValueUncapped = -7f;
                    }
                    else if (Npc.fallValue < -20f)
                    {
                        Npc.isCrouching = false;
                        Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_CROUCHING, false);
                        Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, true);
                    }
                }
                if (Npc.fallValueUncapped < -35f)
                {
                    Npc.takingFallDamage = true;
                }
            }
            else
            {
                movementHinderedPrev = Npc.isMovementHindered;
                if (!IsJumping)
                {
                    if (isFallingNoJump)
                    {
                        isFallingNoJump = false;
                        if (!Npc.isCrouching && Npc.fallValue < -9f)
                        {
                            Npc.playerBodyAnimator.SetTrigger(Const.PLAYER_ANIMATION_TRIGGER_SHORTFALLLANDING);
                        }
                        Plugin.LogDebug($"{Npc.playerUsername} JustTouchedGround GroundHit.point {GroundHit.point} fallValue {Npc.fallValue}");
                        PlayerControllerBPatch.PlayerHitGroundEffects_ReversePatch(this.Npc);
                    }
                    if (!IsFallingFromJump)
                    {
                        Npc.fallValue = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                        Npc.fallValueUncapped = -7f - Mathf.Clamp(12f * slopeModifier, 0f, 100f);
                    }
                }
                Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_FALLNOJUMP, false);
            }
        }

        /// <summary>
        /// Update movement when using jetpack controls
        /// </summary>
        private void UpdateJetPackMoveValuesForOwner()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            if (Npc.jetpackControls || Npc.disablingJetpackControls)
            {
                if (!this.TeleportingThisFrame && !Npc.inSpecialInteractAnimation && !Npc.enteringSpecialAnimation && !Npc.isClimbingLadder && (instanceSOR.timeSinceRoundStarted > 1f || instanceSOR.testRoom != null))
                {
                    float magnitude2 = Npc.thisController.velocity.magnitude;
                    if (Npc.getAverageVelocityInterval <= 0f)
                    {
                        Npc.getAverageVelocityInterval = 0.04f;
                        Npc.velocityAverageCount++;
                        if (Npc.velocityAverageCount > Npc.velocityMovingAverageLength)
                        {
                            Npc.averageVelocity += (magnitude2 - Npc.averageVelocity) / (float)(Npc.velocityMovingAverageLength + 1);
                        }
                        else
                        {
                            Npc.averageVelocity += magnitude2;
                            if (Npc.velocityAverageCount == Npc.velocityMovingAverageLength)
                            {
                                Npc.averageVelocity /= (float)Npc.velocityAverageCount;
                            }
                        }
                    }
                    else
                    {
                        Npc.getAverageVelocityInterval -= Time.deltaTime;
                    }
                    if (TimeSinceTakingGravityDamage > 0.6f && Npc.velocityAverageCount > 4)
                    {
                        float num8 = Vector3.Angle(Npc.transform.up, Vector3.up);
                        if (Physics.CheckSphere(Npc.gameplayCamera.transform.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)
                            || (num8 > 65f && Physics.CheckSphere(Npc.lowerSpine.position, 0.5f, instanceSOR.collidersAndRoomMaskAndDefault, QueryTriggerInteraction.Ignore)))
                        {
                            if (Npc.averageVelocity > 17f)
                            {
                                TimeSinceTakingGravityDamage = 0f;
                                this.InternAIController.SyncDamageIntern(Mathf.Clamp(85, 20, 100), CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                            }
                            else if (Npc.averageVelocity > 9f)
                            {
                                this.InternAIController.SyncDamageIntern(Mathf.Clamp(30, 20, 100), CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                                TimeSinceTakingGravityDamage = 0.35f;
                            }
                            else if (num8 > 60f && Npc.averageVelocity > 6f)
                            {
                                this.InternAIController.SyncDamageIntern(Mathf.Clamp(30, 20, 100), CauseOfDeath.Gravity, 0, true, Vector3.ClampMagnitude(Npc.velocityLastFrame, 50f));
                                TimeSinceTakingGravityDamage = 0f;
                            }
                        }
                    }
                    else
                    {
                        TimeSinceTakingGravityDamage += Time.deltaTime;
                    }
                    Npc.velocityLastFrame = Npc.thisController.velocity;
                    PreviousFrameDeltaTime = Time.deltaTime;
                }
                else
                {
                    TeleportingThisFrame = false;
                }
            }
            else
            {
                Npc.averageVelocity = 0f;
                Npc.velocityAverageCount = 0;
                TimeSinceTakingGravityDamage = 0f;
            }
        }

        /// <summary>
        /// Update movement when using ladder
        /// </summary>
        private void UpdateMoveWhenClimbingLadder()
        {
            Vector3 direction = Npc.thisPlayerBody.up;
            Vector3 origin = Npc.gameplayCamera.transform.position + Npc.thisPlayerBody.up * 0.07f;
            if ((Npc.externalForces + Npc.externalForceAutoFade).magnitude > 8f)
            {
                Npc.CancelSpecialTriggerAnimations();
            }
            Npc.externalForces = Vector3.zero;
            Npc.externalForceAutoFade = Vector3.Lerp(Npc.externalForceAutoFade, Vector3.zero, 5f * Time.deltaTime);

            if (goDownLadder)
            {
                direction = -Npc.thisPlayerBody.up;
                origin = Npc.gameplayCamera.transform.position;
            }
            if (!Physics.Raycast(origin, direction, 0.15f, StartOfRound.Instance.allPlayersCollideWithMask, QueryTriggerInteraction.Ignore))
            {
                Npc.thisPlayerBody.transform.position += direction * (Const.BASE_MAX_SPEED * Npc.climbSpeed * Time.deltaTime);
            }
        }

        private void UpdateAnimationsForOwner()
        {
            //Plugin.LogDebug($"animationSpeed {Npc.playerBodyAnimator.GetFloat("animationSpeed")}");
            //for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
            //{
            //    Plugin.LogDebug($"layer {i}, {Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash}");
            //}


            // Update the "what should be the animation state"
            // Layer 0
            if (Npc.isCrouching)
            {
                if (IsWalking)
                {
                    animationHashLayers[0] = Const.CROUCHING_WALKING_STATE_HASH;
                }
                else
                {
                    animationHashLayers[0] = Const.CROUCHING_IDLE_STATE_HASH;
                }
            }
            else if (Npc.isSprinting)
            {
                animationHashLayers[0] = Const.SPRINTING_STATE_HASH;
            }
            else if (IsWalking)
            {
                animationHashLayers[0] = Const.WALKING_STATE_HASH;
            }
            else
            {
                animationHashLayers[0] = Const.IDLE_STATE_HASH;
            }

            if (IsControllerInCruiser)
            {
                animationHashLayers[0] = Const.IDLE_STATE_HASH;
            }

            // Other layers
            for (int i = 1; i < Npc.playerBodyAnimator.layerCount; i++)
            {
                animationHashLayers[i] = Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(i).fullPathHash;
            }

            if (NetworkManager.Singleton != null && Npc.playersManager.connectedPlayersAmount > 0)
            {
                // Sync
                UpdateInternAnimationsToOtherClients(animationHashLayers);
            }

            if (ShouldAnimate)
            {
                if (Npc.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_WALKING) != IsWalking)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, IsWalking);
                }
                if (Npc.playerBodyAnimator.GetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING) != Npc.isSprinting)
                {
                    Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, Npc.isSprinting);
                }
            }
            else
            {
                CutAnimations();
            }
        }

        #endregion

        #region Updates npc body for not owner

        /// <summary>
        /// Sync the position with the server position and the rotations
        /// </summary>
        private void UpdateSyncPositionAndRotationForNotOwner()
        {
            if (!Npc.isPlayerDead && Npc.isPlayerControlled)
            {
                if (!Npc.disableSyncInAnimation)
                {
                    if (Npc.snapToServerPosition)
                    {
                        Npc.transform.localPosition = Vector3.Lerp(Npc.transform.localPosition, Npc.serverPlayerPosition, 16f * Time.deltaTime);
                    }
                    else
                    {
                        float num10 = 8f;
                        if (Npc.jetpackControls)
                        {
                            num10 = 15f;
                        }
                        float num11 = Mathf.Clamp(num10 * Vector3.Distance(Npc.transform.localPosition, Npc.serverPlayerPosition), 0.9f, 300f);
                        Npc.transform.localPosition = Vector3.MoveTowards(Npc.transform.localPosition, Npc.serverPlayerPosition, num11 * Time.deltaTime);
                    }
                }

                // Rotations
                this.UpdateTurnBodyTowardsDirection();
                this.UpdateLookAt();
                Npc.playerEye.position = Npc.gameplayCamera.transform.position;
                Npc.playerEye.rotation = Npc.gameplayCamera.transform.rotation;
            }
            else if ((Npc.isPlayerDead || !Npc.isPlayerControlled) && Npc.setPositionOfDeadPlayer)
            {
                Npc.transform.position = Npc.playersManager.notSpawnedPosition.position;
            }
        }

        private void UpdateInternAnimationsLocalForNotOwner(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            if (Npc.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;

                if (ShouldAnimate)
                {
                    // If animation
                    // Update animation if current != previous
                    this.currentAnimationSpeed = Npc.playerBodyAnimator.GetFloat("animationSpeed");
                    for (int i = 0; i < animationsStateHash.Length; i++)
                    {
                        this.currentAnimationStateHash[i] = animationsStateHash[i];
                        if (this.previousAnimationStateHash[i] != this.currentAnimationStateHash[i])
                        {
                            this.previousAnimationStateHash[i] = this.currentAnimationStateHash[i];
                            this.previousAnimationSpeed = this.currentAnimationSpeed;
                            ApplyUpdateInternAnimationsNotOwner(this.currentAnimationStateHash[i], this.currentAnimationSpeed);
                            return;
                        }
                    }
                }
                else
                {
                    // If no animation
                    // Return to idle state and keep previous animation state to idle, for an update if animation resume
                    if (Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH)
                    {
                        for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                        {
                            if (Npc.playerBodyAnimator.HasState(i, Const.IDLE_STATE_HASH))
                            {
                                this.previousAnimationStateHash[i] = Const.IDLE_STATE_HASH;
                                Npc.playerBodyAnimator.CrossFadeInFixedTime(Const.IDLE_STATE_HASH, 0.1f);
                                return;
                            }
                        }
                    }
                }

                if (this.previousAnimationSpeed != this.currentAnimationSpeed)
                {
                    this.previousAnimationSpeed = this.currentAnimationSpeed;
                    ApplyUpdateInternAnimationsNotOwner(0, this.currentAnimationSpeed);
                }
            }
        }

        #endregion

        #region Updates npc body for all (owner and not owner)

        /// <summary>
        /// Update the localarms and rotation when in special interact animation
        /// </summary>
        private void UpdateInSpecialInteractAnimationEffect()
        {
            if (!Npc.inSpecialInteractAnimation)
            {
                if (Npc.playingQuickSpecialAnimation)
                {
                    Npc.specialAnimationWeight = 1f;
                }
                else
                {
                    Npc.specialAnimationWeight = Mathf.Lerp(Npc.specialAnimationWeight, 0f, Time.deltaTime * 12f);
                }
                if (!Npc.localArmsMatchCamera)
                {
                    Npc.localArmsTransform.position = Npc.playerModelArmsMetarig.position + Npc.playerModelArmsMetarig.forward * -0.445f;
                    Npc.playerModelArmsMetarig.rotation = Quaternion.Lerp(Npc.playerModelArmsMetarig.rotation, Npc.localArmsRotationTarget.rotation, 15f * Time.deltaTime);
                }
            }
            else
            {
                if ((!Npc.isClimbingLadder && !Npc.inShockingMinigame) || Npc.freeRotationInInteractAnimation)
                {
                    CameraUp = Mathf.Lerp(CameraUp, 0f, 5f * Time.deltaTime);
                    Npc.gameplayCamera.transform.localEulerAngles = new Vector3(CameraUp, Npc.gameplayCamera.transform.localEulerAngles.y, Npc.gameplayCamera.transform.localEulerAngles.z);
                }
                Npc.specialAnimationWeight = Mathf.Lerp(Npc.specialAnimationWeight, 1f, Time.deltaTime * 20f);
                Npc.playerModelArmsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);
            }
        }
        /// <summary>
        /// Update animation layer when using emotes
        /// </summary>
        private void UpdateEmoteEffects()
        {
            if (Npc.doingUpperBodyEmote > 0f)
            {
                Npc.doingUpperBodyEmote -= Time.deltaTime;
            }

            if (Npc.performingEmote)
            {
                Npc.emoteLayerWeight = Mathf.Lerp(Npc.emoteLayerWeight, 1f, 10f * Time.deltaTime);
            }
            else
            {
                Npc.emoteLayerWeight = Mathf.Lerp(Npc.emoteLayerWeight, 0f, 10f * Time.deltaTime);
            }
            Npc.playerBodyAnimator.SetLayerWeight(Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_EMOTESNOARMS), Npc.emoteLayerWeight);
        }
        /// <summary>
        /// Update the sinking values and effect
        /// </summary>
        private void UpdateSinkingEffects()
        {
            StartOfRound instanceSOR = StartOfRound.Instance;

            Npc.meshContainer.position = Vector3.Lerp(Npc.transform.position, Npc.transform.position - Vector3.up * 2.8f, instanceSOR.playerSinkingCurve.Evaluate(Npc.sinkingValue));
            if (Npc.isSinking && !Npc.inSpecialInteractAnimation && Npc.inAnimationWithEnemy == null)
            {
                Npc.sinkingValue = Mathf.Clamp(Npc.sinkingValue + Time.deltaTime * Npc.sinkingSpeedMultiplier, 0f, 1f);
            }
            else
            {
                Npc.sinkingValue = Mathf.Clamp(Npc.sinkingValue - Time.deltaTime * 0.75f, 0f, 1f);
            }
            if (Npc.sinkingValue > 0.73f || Npc.isUnderwater)
            {
                if (!this.wasUnderwaterLastFrame)
                {
                    this.wasUnderwaterLastFrame = true;
                    Npc.waterBubblesAudio.Play();
                }
                Npc.voiceMuffledByEnemy = true;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                OccludeAudioComponent.overridingLowPass = true;
                OccludeAudioComponent.lowPassOverride = 600f;
                Npc.waterBubblesAudio.volume = Mathf.Clamp(Npc.currentVoiceChatAudioSource.volume, 0f, 1f);
            }
            else if (this.wasUnderwaterLastFrame)
            {
                Npc.waterBubblesAudio.Stop();
                this.wasUnderwaterLastFrame = false;
                Npc.voiceMuffledByEnemy = false;
            }
            else
            {
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
            }
        }
        /// <summary>
        /// Update the active audio reverb filter
        /// </summary>
        private void UpdateActiveAudioReverbFilter()
        {
            //GameNetworkManager instanceGNM = GameNetworkManager.Instance;
            //StartOfRound instanceSOR = StartOfRound.Instance;

            //if (Npc.activeAudioReverbFilter == null)
            //{
            //    Npc.activeAudioReverbFilter = Npc.activeAudioListener.GetComponent<AudioReverbFilter>();
            //    Npc.activeAudioReverbFilter.enabled = true;
            //}
            //if (Npc.reverbPreset != null && instanceGNM != null && instanceGNM.localPlayerController != null
            //    && ((instanceGNM.localPlayerController == this.Npc
            //    && (!Npc.isPlayerDead || instanceSOR.overrideSpectateCamera)) || (instanceGNM.localPlayerController.spectatedPlayerScript == this.Npc && !instanceSOR.overrideSpectateCamera)))
            //{
            //    Npc.activeAudioReverbFilter.dryLevel = Mathf.Lerp(Npc.activeAudioReverbFilter.dryLevel, Npc.reverbPreset.dryLevel, 15f * Time.deltaTime);
            //    Npc.activeAudioReverbFilter.roomLF = Mathf.Lerp(Npc.activeAudioReverbFilter.roomLF, Npc.reverbPreset.lowFreq, 15f * Time.deltaTime);
            //    Npc.activeAudioReverbFilter.roomLF = Mathf.Lerp(Npc.activeAudioReverbFilter.roomHF, Npc.reverbPreset.highFreq, 15f * Time.deltaTime);
            //    Npc.activeAudioReverbFilter.decayTime = Mathf.Lerp(Npc.activeAudioReverbFilter.decayTime, Npc.reverbPreset.decayTime, 15f * Time.deltaTime);
            //    Npc.activeAudioReverbFilter.room = Mathf.Lerp(Npc.activeAudioReverbFilter.room, Npc.reverbPreset.room, 15f * Time.deltaTime);
            //}
        }
        /// <summary>
        /// Update animations when holding items and exhausion
        /// </summary>
        private void UpdateAnimationUpperBody()
        {
            int indexLayerHoldingItemsRightHand = Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSRIGHTHAND);
            int indexLayerHoldingItemsBothHands = Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_HOLDINGITEMSBOTHHANDS);

            if (Npc.isHoldingObject || Npc.isGrabbingObjectAnimation || Npc.inShockingMinigame)
            {
                this.upperBodyAnimationsWeight = Mathf.Lerp(this.upperBodyAnimationsWeight, 1f, 25f * Time.deltaTime);
                if (Npc.twoHandedAnimation || Npc.inShockingMinigame)
                {
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, Mathf.Abs(this.upperBodyAnimationsWeight - 1f));
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, this.upperBodyAnimationsWeight);
                }
                else
                {
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, this.upperBodyAnimationsWeight);
                    Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, Mathf.Abs(this.upperBodyAnimationsWeight - 1f));
                }
            }
            else
            {
                this.upperBodyAnimationsWeight = Mathf.Lerp(this.upperBodyAnimationsWeight, 0f, 25f * Time.deltaTime);
                Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsRightHand, this.upperBodyAnimationsWeight);
                Npc.playerBodyAnimator.SetLayerWeight(indexLayerHoldingItemsBothHands, this.upperBodyAnimationsWeight);
            }

            Npc.playerBodyAnimator.SetLayerWeight(Npc.playerBodyAnimator.GetLayerIndex(Const.PLAYER_ANIMATION_WEIGHT_SPECIALANIMATIONS), Npc.specialAnimationWeight);
            if (Npc.inSpecialInteractAnimation && !Npc.inShockingMinigame)
            {
                Npc.cameraLookRig1.weight = Mathf.Lerp(Npc.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
                Npc.cameraLookRig2.weight = Mathf.Lerp(Npc.cameraLookRig1.weight, 0f, Time.deltaTime * 25f);
            }
            else
            {
                Npc.cameraLookRig1.weight = 0.45f;
                Npc.cameraLookRig2.weight = 1f;
            }
            if (Npc.isExhausted)
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 1f, 10f * Time.deltaTime);
            }
            else
            {
                this.exhaustionEffectLerp = Mathf.Lerp(this.exhaustionEffectLerp, 0f, 10f * Time.deltaTime);
            }
            Npc.playerBodyAnimator.SetFloat(Const.PLAYER_ANIMATION_FLOAT_TIREDAMOUNT, this.exhaustionEffectLerp);
        }

        #endregion

        #region Animations

        private void UpdateInternAnimationsToOtherClients(int[] animationsStateHash)
        {
            this.updatePlayerAnimationsInterval += Time.deltaTime;
            if (Npc.inSpecialInteractAnimation || this.updatePlayerAnimationsInterval > 0.14f)
            {
                this.updatePlayerAnimationsInterval = 0f;
                this.currentAnimationSpeed = Npc.playerBodyAnimator.GetFloat("animationSpeed");
                for (int i = 0; i < animationsStateHash.Length; i++)
                {
                    this.currentAnimationStateHash[i] = animationsStateHash[i];
                    if (this.previousAnimationStateHash[i] != this.currentAnimationStateHash[i])
                    {
                        this.previousAnimationStateHash[i] = this.currentAnimationStateHash[i];
                        this.previousAnimationSpeed = this.currentAnimationSpeed;
                        InternAIController.UpdateInternAnimationServerRpc(this.currentAnimationStateHash[i], this.currentAnimationSpeed);
                        return;
                    }
                }

                if (this.previousAnimationSpeed != this.currentAnimationSpeed)
                {
                    this.previousAnimationSpeed = this.currentAnimationSpeed;
                    InternAIController.UpdateInternAnimationServerRpc(0, this.currentAnimationSpeed);
                }
            }
        }

        public void ApplyUpdateInternAnimationsNotOwner(int animationState, float animationSpeed)
        {
            if (Npc.playerBodyAnimator.GetFloat("animationSpeed") != animationSpeed)
            {
                Npc.playerBodyAnimator.SetFloat("animationSpeed", animationSpeed);
            }

            if (ShouldAnimate)
            {
                if (animationState != 0 && Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != animationState)
                {
                    for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                    {
                        if (Npc.playerBodyAnimator.HasState(i, animationState))
                        {
                            animationHashLayers[i] = animationState;
                            Npc.playerBodyAnimator.CrossFadeInFixedTime(animationState, 0.1f);
                            return;
                        }
                    }
                }
            }
            else
            {
                if (Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH)
                {
                    for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                    {
                        if (Npc.playerBodyAnimator.HasState(i, Const.IDLE_STATE_HASH))
                        {
                            Npc.playerBodyAnimator.CrossFadeInFixedTime(Const.IDLE_STATE_HASH, 0.1f);
                            break;
                        }
                    }
                }

                for (int i = 0; i < Npc.playerBodyAnimator.layerCount; i++)
                {
                    if (Npc.playerBodyAnimator.HasState(i, animationState))
                    {
                        animationHashLayers[i] = animationState;
                        break;
                    }
                }
            }
        }

        public void StopAnimations()
        {
            IsWalking = false;
            Npc.isSprinting = false;
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
        }

        private void CutAnimations()
        {
            Npc.playerBodyAnimator.SetInteger("emoteNumber", 0);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_WALKING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SPRINTING, false);
            Npc.playerBodyAnimator.SetBool(Const.PLAYER_ANIMATION_BOOL_SIDEWAYS, false);
        }

        private void PlayFootstepIfCloseNoAnimation()
        {
            if (ShouldAnimate)
            {
                return;
            }

            if ((StartOfRound.Instance.localPlayerController.transform.position - Npc.transform.position).sqrMagnitude > 20f * 20f)
            {
                return;
            }

            float threshold = 0f;
            if (animationHashLayers[0] == Const.WALKING_STATE_HASH)
            {
                threshold = 0.498f;
            }
            else if (animationHashLayers[0] == Const.SPRINTING_STATE_HASH)
            {
                threshold = 0.170f + Random.Range(0f, 0.070f);
            }

            if (threshold > 0f)
            {
                timerPlayFootstep += Time.deltaTime;
                if (timerPlayFootstep > threshold)
                {
                    timerPlayFootstep = 0f;

                    if (!InternManager.Instance.CanPlayFootStepSoundAtTheSameTime())
                    {
                        return;
                    }
                    InternManager.Instance.AddFootStepSoundAtTheSameTime();

                    bool noiseIsInsideClosedShip = Npc.isInHangarShipRoom && Npc.playersManager.hangarDoorsClosed;
                    if (animationHashLayers[0] == Const.SPRINTING_STATE_HASH)
                    {
                        RoundManager.Instance.PlayAudibleNoise(Npc.transform.position, 22f, 0.6f, 0, noiseIsInsideClosedShip, 6);
                    }
                    else
                    {
                        RoundManager.Instance.PlayAudibleNoise(Npc.transform.position, 17f, 0.4f, 0, noiseIsInsideClosedShip, 6);
                    }

                    Npc.PlayFootstepSound();
                }
            }
        }

        #endregion

        /// <summary>
        /// LateUpdate called from <see cref="PlayerControllerBPatch.LateUpdate_PreFix"><c>PlayerControllerBPatch.LateUpdate_PreFix</c></see> 
        /// instead of the real LateUpdate from <c>PlayerControllerB</c>.
        /// </summary>
        /// <remarks>
        /// Update username billboard, intern looking target, intern position to clients and other stuff
        /// </remarks>
        public void LateUpdate()
        {
            GameNetworkManager instanceGNM = GameNetworkManager.Instance;

            Npc.previousElevatorPosition = Npc.playersManager.elevatorTransform.position;

            if (NetworkManager.Singleton == null)
            {
                return;
            }

            // Text billboard
            Npc.usernameBillboardText.text = InternAIController.GetSizedBillboardStateIndicator();
            if (timerShowName >= 0f)
            {
                timerShowName -= Time.deltaTime;
                Npc.usernameBillboardText.text += $"\n{Npc.playerUsername}";

                if (InternAIController.IsClientOwnerOfIntern())
                {
                    Npc.usernameBillboardText.text += $"\nv";
                }
            }

            if (instanceGNM.localPlayerController != null)
            {
                Npc.usernameBillboard.LookAt(instanceGNM.localPlayerController.localVisorTargetPoint);
            }

            // Physics regions
            //int priority = 0;
            //Transform? transform = null;
            //for (int i = 0; i < CurrentInternPhysicsRegions.Count; i++)
            //{
            //    if (CurrentInternPhysicsRegions[i].priority > priority)
            //    {
            //        priority = CurrentInternPhysicsRegions[i].priority;
            //        transform = CurrentInternPhysicsRegions[i].physicsTransform;
            //    }
            //}
            //if (Npc.isInElevator && priority <= 0)
            //{
            //    transform = null;
            //}
            //Npc.physicsParent = transform;

            //if (Npc.physicsParent != null)
            //{
            //    ReParentNotSpawnedTransform(Npc.physicsParent);
            //}
            //else
            //{
            //    if (Npc.isInElevator)
            //    {
            //        ReParentNotSpawnedTransform(Npc.playersManager.elevatorTransform);
            //        if (!InternAIController.AreHandsFree())
            //        {
            //            Npc.SetItemInElevator(Npc.isInHangarShipRoom, Npc.isInElevator, InternAIController.HeldItem);
            //        }
            //    }
            //    else
            //    {
            //        if (!IsControllerInCruiser)
            //        {
            //            ReParentNotSpawnedTransform(Npc.playersManager.playersContainer);
            //        }
            //    }
            //}

            // Health regen
            InternAIController.HealthRegen();

            if (InternAIController.IsClientOwnerOfIntern())
            {
                this.InternRotationAndLookUpdate();

                if (Npc.isPlayerControlled && !Npc.isPlayerDead)
                {
                    if (instanceGNM != null)
                    {
                        float distMaxBeforeUpdating;
                        if (Npc.inSpecialInteractAnimation)
                        {
                            distMaxBeforeUpdating = 0.06f;
                        }
                        else if (IsRealPlayerClose(Npc.transform.position, 10f))
                        {
                            distMaxBeforeUpdating = 0.1f;
                        }
                        else
                        {
                            distMaxBeforeUpdating = 0.24f;
                        }

                        if ((Npc.oldPlayerPosition - Npc.transform.localPosition).sqrMagnitude > distMaxBeforeUpdating || UpdatePositionForNewlyJoinedClient)
                        {
                            UpdatePositionForNewlyJoinedClient = false;
                            if (!Npc.playersManager.newGameIsLoading)
                            {
                                InternAIController.SyncUpdateInternPosition(Npc.thisPlayerBody.localPosition, Npc.isInElevator, Npc.isInHangarShipRoom, Npc.isExhausted, IsTouchingGround);
                                Npc.serverPlayerPosition = Npc.transform.localPosition;
                                Npc.oldPlayerPosition = Npc.serverPlayerPosition;
                            }
                        }

                        GrabbableObject? currentlyHeldObject = InternAIController.HeldItem;
                        if (currentlyHeldObject != null && Npc.isHoldingObject && this.GrabbedObjectValidated)
                        {
                            currentlyHeldObject.transform.localPosition = currentlyHeldObject.itemProperties.positionOffset;
                            currentlyHeldObject.transform.localEulerAngles = currentlyHeldObject.itemProperties.rotationOffset;
                        }
                    }

                    float num2 = 1f;
                    if (Npc.drunkness > 0.02f)
                    {
                        num2 *= Mathf.Abs(StartOfRound.Instance.drunknessSpeedEffect.Evaluate(Npc.drunkness) - 1.25f);
                    }
                    if (Npc.isSprinting)
                    {
                        // Cut exhaustion for now
                        //Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter - Time.deltaTime / Npc.sprintTime * Npc.carryWeight * num2, 0f, 1f);
                    }
                    else if (Npc.isMovementHindered > 0)
                    {
                        if (IsWalking)
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter - Time.deltaTime / Npc.sprintTime * num2 * 0.5f, 0f, 1f);
                        }
                    }
                    else
                    {
                        if (!IsWalking)
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter + Time.deltaTime / (Npc.sprintTime + 4f) * num2, 0f, 1f);
                        }
                        else
                        {
                            Npc.sprintMeter = Mathf.Clamp(Npc.sprintMeter + Time.deltaTime / (Npc.sprintTime + 9f) * num2, 0f, 1f);
                        }
                        if (Npc.isExhausted && Npc.sprintMeter > 0.2f)
                        {
                            Npc.isExhausted = false;
                        }
                    }
                }
            }
            if (!Npc.inSpecialInteractAnimation && Npc.localArmsMatchCamera)
            {
                Npc.localArmsTransform.position = Npc.cameraContainerTransform.transform.position + Npc.gameplayCamera.transform.up * -0.5f;
                Npc.playerModelArmsMetarig.rotation = Npc.localArmsRotationTarget.rotation;
            }
        }

        public void ReParentNotSpawnedTransform(Transform newParent)
        {
            if (Npc.transform.parent != newParent)
            {
                foreach (NetworkObject networkObject in Npc.GetComponentsInChildren<NetworkObject>())
                {
                    networkObject.AutoObjectParentSync = false;
                }

                Plugin.LogDebug($"{Npc.playerUsername} ReParent parent before {Npc.transform.parent}");
                Npc.transform.parent = newParent;
                Plugin.LogDebug($"{Npc.playerUsername} ReParent parent after {Npc.transform.parent}");

                foreach (NetworkObject networkObject in Npc.GetComponentsInChildren<NetworkObject>())
                {
                    networkObject.AutoObjectParentSync = true;
                }
            }
        }

        public bool CheckConditionsForSinkingInQuicksandIntern()
        {
            if (!IsTouchingGround)
            {
                return false;
            }

            if (Npc.inSpecialInteractAnimation || (bool)Npc.inAnimationWithEnemy || Npc.isClimbingLadder)
            {
                return false;
            }

            if (Npc.physicsParent != null)
            {
                return false;
            }

            if (Npc.isInHangarShipRoom)
            {
                return false;
            }

            if (Npc.isInElevator)
            {
                return false;
            }

            if (Npc.currentFootstepSurfaceIndex != 1
                && Npc.currentFootstepSurfaceIndex != 4
                && Npc.currentFootstepSurfaceIndex != 8
                && (!Npc.isInsideFactory || Npc.currentFootstepSurfaceIndex != 5))
            {
                return false;
            }

            return true;
        }

        private bool IsRealPlayerClose(Vector3 thisPosition, float distance)
        {
            for (int i = 0; i < InternManager.Instance.IndexBeginOfInterns; i++)
            {
                if ((StartOfRound.Instance.allPlayerScripts[i].transform.position - thisPosition).sqrMagnitude < distance * distance)
                {
                    return true;
                }
            }
            return false;
        }

        #region Emotes

        public void MimicEmotes(PlayerControllerB playerToMimic)
        {
            if (playerToMimic.performingEmote)
            {
                if (Plugin.IsModTooManyEmotesLoaded)
                {
                    CheckAndPerformTooManyEmote(playerToMimic);
                }
                else
                {
                    PerformDefaultEmote(playerToMimic.playerBodyAnimator.GetInteger("emoteNumber"));
                }
            }
            else
            {
                if (Npc.performingEmote)
                {
                    Npc.performingEmote = false;
                    Npc.playerBodyAnimator.SetInteger("emoteNumber", 0);
                    this.InternAIController.SyncStopPerformingEmote();
                    if (Plugin.IsModTooManyEmotesLoaded)
                    {
                        StopPerformingTooManyEmote();
                    }
                }
            }
        }

        private void CheckAndPerformTooManyEmote(PlayerControllerB playerToMimic)
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerPlayerOfplayerToMimic = playerToMimic.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerPlayerOfplayerToMimic == null)
            {
                return;
            }
            TooManyEmotes.EmoteControllerPlayer emoteControllerIntern = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerIntern == null)
            {
                return;
            }

            // Player performing emote but not tooManyEmote so default
            if (!emoteControllerPlayerOfplayerToMimic.isPerformingEmote)
            {
                if (emoteControllerIntern.isPerformingEmote)
                {
                    emoteControllerIntern.StopPerformingEmote();
                    InternAIController.StopPerformTooManyEmoteInternServerRpc();
                }

                // Default emote
                PerformDefaultEmote(playerToMimic.playerBodyAnimator.GetInteger("emoteNumber"));
                return;
            }

            // TooMany emotes
            if (emoteControllerPlayerOfplayerToMimic.performingEmote == null)
            {
                return;
            }

            if (emoteControllerIntern.isPerformingEmote
                && emoteControllerPlayerOfplayerToMimic.performingEmote.emoteId == emoteControllerIntern.performingEmote?.emoteId)
            {
                return;
            }

            // PerformEmote TooMany emote
            InternAIController.PerformTooManyEmoteInternServerRpc(emoteControllerPlayerOfplayerToMimic.performingEmote.emoteId);
        }

        private void PerformDefaultEmote(int emoteNumberToMimic)
        {
            int emoteNumberIntern = Npc.playerBodyAnimator.GetInteger("emoteNumber");
            if (!Npc.performingEmote
                || emoteNumberIntern != emoteNumberToMimic)
            {
                Npc.performingEmote = true;
                Npc.PerformEmote(new UnityEngine.InputSystem.InputAction.CallbackContext(), emoteNumberToMimic);
            }
        }

        public void PerformTooManyEmote(int tooManyEmoteID)
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerIntern = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerIntern == null)
            {
                return;
            }

            if (emoteControllerIntern.isPerformingEmote)
            {
                emoteControllerIntern.StopPerformingEmote();
            }

            TooManyEmotes.UnlockableEmote unlockableEmote = TooManyEmotes.EmotesManager.allUnlockableEmotes[tooManyEmoteID];
            emoteControllerIntern.PerformEmote(unlockableEmote);
        }

        public void StopPerformingTooManyEmote()
        {
            TooManyEmotes.EmoteControllerPlayer emoteControllerInternController = Npc.gameObject.GetComponent<TooManyEmotes.EmoteControllerPlayer>();
            if (emoteControllerInternController != null)
            {
                emoteControllerInternController.StopPerformingEmote();
            }
        }

        #endregion

        /// <summary>
        /// Sync the rotation and the look at target to all clients
        /// </summary>
        private void InternRotationAndLookUpdate()
        {
            if (!Npc.isPlayerControlled)
            {
                return;
            }

            if (Npc.playersManager.connectedPlayersAmount < 1
                || Npc.playersManager.newGameIsLoading
                || Npc.disableLookInput)
            {
                return;
            }

            int newIntEnumObjectsLookingAt = (int)this.enumObjectsLookingAt;
            Vector3 newPlayerEyeToLookAt = this.positionPlayerEyeToLookAt;
            Vector3 newPositionPlayerToLookAt = this.positionToLookAt;
            Vector3 newDirectionToUpdateTurnBodyTowardsTo = this.directionToUpdateTurnBodyTowardsTo;

            if (this.oldSentIntEnumObjectsLookingAt == newIntEnumObjectsLookingAt
                && this.oldSentPositionPlayerEyeToLookAt == newPlayerEyeToLookAt
                && this.oldSentPositionToLookAt == newPositionPlayerToLookAt
                && this.oldSentDirectionToUpdateTurnBodyTowardsTo == newDirectionToUpdateTurnBodyTowardsTo)
            {
                return;
            }

            // Update after some interval of time
            // Only if there's at least one player near
            if (this.UpdatePlayerLookInterval > 0.25f && IsRealPlayerClose(Npc.transform.position, 35f))
            {
                this.UpdatePlayerLookInterval = 0f;
                InternAIController.SyncUpdateInternRotationAndLook(InternAIController.State.GetBillboardStateIndicator(),
                                                                   newDirectionToUpdateTurnBodyTowardsTo,
                                                                   newIntEnumObjectsLookingAt,
                                                                   newPlayerEyeToLookAt,
                                                                   newPositionPlayerToLookAt);

                this.oldSentIntEnumObjectsLookingAt = newIntEnumObjectsLookingAt;
                this.oldSentPositionPlayerEyeToLookAt = newPlayerEyeToLookAt;
                this.oldSentPositionToLookAt = newPositionPlayerToLookAt;
                this.oldSentDirectionToUpdateTurnBodyTowardsTo = newDirectionToUpdateTurnBodyTowardsTo;
            }
        }

        /// <summary>
        /// Set the move vector to go forward
        /// </summary>
        public void OrderToMove()
        {
            this.lastMoveVector = new Vector2(0f, 1f);
        }

        /// <summary>
        /// Set the move vector to 0
        /// </summary>
        public void OrderToStopMoving()
        {
            this.lastMoveVector = Vector2.zero;
            floatSprint = 0f;
        }

        /// <summary>
        /// Set the controller to sprint
        /// </summary>
        public void OrderToSprint()
        {
            if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }
            if (Npc.isSprinting)
            {
                return;
            }

            floatSprint = 1f;
        }
        /// <summary>
        /// Set the controller to stop sprinting
        /// </summary>
        public void OrderToStopSprint()
        {
            if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }
            if (Npc.isSprinting)
            {
                return;
            }

            floatSprint = 0f;
        }
        /// <summary>
        /// Set the controller to crouch on/off
        /// </summary>
        public void OrderToToggleCrouch()
        {
            if (Npc.inSpecialInteractAnimation || !IsTouchingGround || Npc.isClimbingLadder)
            {
                return;
            }
            if (this.IsJumping)
            {
                return;
            }
            if (Npc.isSprinting)
            {
                return;
            }
            this.CrouchMeter = Mathf.Min(this.CrouchMeter + 0.3f, 1.3f);
            Npc.Crouch(!Npc.isCrouching);
        }

        /// <summary>
        /// Set the direction the controller should turn towards, using a vector position
        /// </summary>
        /// <param name="positionDirection">Position to turn to</param>
        public void SetTurnBodyTowardsDirectionWithPosition(Vector3 positionDirection)
        {
            directionToUpdateTurnBodyTowardsTo = positionDirection - Npc.thisController.transform.position;
        }
        /// <summary>
        /// Set the direction the controller should turn towards, using a vector direction
        /// </summary>
        /// <param name="direction">Direction to turn to</param>
        public void SetTurnBodyTowardsDirection(Vector3 direction)
        {
            directionToUpdateTurnBodyTowardsTo = direction;
        }

        /// <summary>
        /// Turn the body towards the direction set beforehand
        /// </summary>
        private void UpdateTurnBodyTowardsDirection()
        {
            if (IsControllerInCruiser)
            {
                return;
            }

            UpdateNowTurnBodyTowardsDirection(directionToUpdateTurnBodyTowardsTo);
        }

        public void UpdateNowTurnBodyTowardsDirection(Vector3 direction)
        {
            if (DirectionNotZero(direction.x) || DirectionNotZero(direction.z))
            {
                Quaternion targetRotation = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z));
                Npc.thisPlayerBody.rotation = Quaternion.Lerp(Npc.thisPlayerBody.rotation, targetRotation, Const.BODY_TURNSPEED * Time.deltaTime);
            }
        }

        /// <summary>
        /// Make the controller look at the eyes of a player
        /// </summary>
        /// <param name="positionPlayerEyeToLookAt"></param>
        public void OrderToLookAtPlayer(Vector3 positionPlayerEyeToLookAt)
        {
            this.enumObjectsLookingAt = EnumObjectsLookingAt.Player;
            this.positionPlayerEyeToLookAt = positionPlayerEyeToLookAt;
        }
        /// <summary>
        /// Make the controller look straight forward
        /// </summary>
        public void OrderToLookForward()
        {
            this.enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
        }
        /// <summary>
        /// Make the controller look at an specific vector position
        /// </summary>
        /// <param name="positionToLookAt"></param>
        public void OrderToLookAtPosition(Vector3 positionToLookAt)
        {
            this.enumObjectsLookingAt = EnumObjectsLookingAt.Position;
            this.positionToLookAt = positionToLookAt;
        }
        /// <summary>
        /// Update the head of the intern to look at what he is set to
        /// </summary>
        private void UpdateLookAt()
        {
            if (IsMoving())
            {
                return;
            }

            Vector3 direction;
            switch (enumObjectsLookingAt)
            {
                case EnumObjectsLookingAt.Forward:

                    Npc.gameplayCamera.transform.rotation = Quaternion.RotateTowards(Npc.gameplayCamera.transform.rotation, Npc.thisPlayerBody.rotation, Const.CAMERA_TURNSPEED);
                    break;

                case EnumObjectsLookingAt.Player:

                    direction = positionPlayerEyeToLookAt - Npc.gameplayCamera.transform.position;
                    if (DirectionNotZero(direction.x) || DirectionNotZero(direction.y) || DirectionNotZero(direction.z))
                    {
                        Quaternion cameraRotation = Quaternion.LookRotation(new Vector3(direction.x, direction.y, direction.z));
                        Npc.gameplayCamera.transform.rotation = Quaternion.RotateTowards(Npc.gameplayCamera.transform.rotation, cameraRotation, Const.CAMERA_TURNSPEED);

                        if (Vector3.Angle(Npc.gameplayCamera.transform.forward, Npc.thisPlayerBody.transform.forward) > Const.INTERN_FOV - 5f)
                        {
                            if (this.HasToMove)
                                enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
                            else
                                SetTurnBodyTowardsDirectionWithPosition(positionPlayerEyeToLookAt);
                        }
                    }
                    break;

                case EnumObjectsLookingAt.Position:

                    direction = positionToLookAt - Npc.gameplayCamera.transform.position;
                    if (DirectionNotZero(direction.x) || DirectionNotZero(direction.y) || DirectionNotZero(direction.z))
                    {
                        Quaternion cameraRotation = Quaternion.LookRotation(new Vector3(direction.x, direction.y, direction.z));
                        Npc.gameplayCamera.transform.rotation = Quaternion.RotateTowards(Npc.gameplayCamera.transform.rotation, cameraRotation, Const.CAMERA_TURNSPEED);

                        if (Vector3.Angle(Npc.gameplayCamera.transform.forward, Npc.thisPlayerBody.transform.forward) > Const.INTERN_FOV - 20f)
                        {
                            if (this.HasToMove)
                                enumObjectsLookingAt = EnumObjectsLookingAt.Forward;
                            else
                                SetTurnBodyTowardsDirectionWithPosition(positionToLookAt);
                        }
                    }
                    break;
            }
        }

        public bool IsMoving()
        {
            return MoveVector != Vector3.zero
                || animationHashLayers[0] != Const.IDLE_STATE_HASH
                || Npc.playerBodyAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash != Const.IDLE_STATE_HASH;
        }

        private void ForceTurnTowardsTarget()
        {
            if (Npc.inSpecialInteractAnimation && Npc.inShockingMinigame && Npc.shockingTarget != null)
            {
                OrderToLookAtPosition(Npc.shockingTarget.position);
            }
            else if (Npc.inAnimationWithEnemy
                     && EnemyInAnimationWith != null)
            {
                Vector3 pos;
                if (EnemyInAnimationWith.eye != null)
                {
                    pos = EnemyInAnimationWith.eye.position;
                }
                else
                {
                    pos = EnemyInAnimationWith.transform.position;
                }

                OrderToLookAtPosition(pos);
            }
        }

        /// <summary>
        /// Set the controller to go down or up on the ladder
        /// </summary>
        /// <param name="hasToGoDown"></param>
        public void OrderToGoUpDownLadder(bool hasToGoDown)
        {
            this.goDownLadder = hasToGoDown;
        }

        /// <summary>
        /// Checks if the intern can use the ladder
        /// </summary>
        /// <param name="ladder"></param>
        /// <returns></returns>
        public bool CanUseLadder(InteractTrigger ladder)
        {
            if (ladder.usingLadder)
            {
                return false;
            }

            // todo : ladder item holding configurable ?
            //if ((this.Npc.isHoldingObject && !ladder.oneHandedItemAllowed)
            //    || (this.Npc.twoHanded &&
            //                       (!ladder.twoHandedItemAllowed || ladder.specialCharacterAnimation)))
            //{
            //    Plugin.LogDebug("no ladder cuz holding things");
            //    return false;
            //}

            if (this.Npc.sinkingValue > 0.73f)
            {
                return false;
            }
            if (this.Npc.jetpackControls && (ladder.specialCharacterAnimation || ladder.isLadder))
            {
                return false;
            }
            if (this.Npc.isClimbingLadder)
            {
                if (ladder.isLadder)
                {
                    if (!ladder.usingLadder)
                    {
                        return false;
                    }
                }
                else if (ladder.specialCharacterAnimation)
                {
                    return false;
                }
            }
            else if (this.Npc.inSpecialInteractAnimation)
            {
                return false;
            }

            if (ladder.isPlayingSpecialAnimation)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Save the different animation for an item and the state
        /// </summary>
        /// <param name="animationString">Name of the animation</param>
        /// <param name="value">active or not</param>
        public void SetAnimationBoolForItem(string animationString, bool value)
        {
            if (dictAnimationBoolPerItem == null)
            {
                dictAnimationBoolPerItem = new Dictionary<string, bool>();
            }

            dictAnimationBoolPerItem[animationString] = value;
        }

        public void ShowFullNameBillboard()
        {
            timerShowName = 1f;
        }

        /// <summary>
        /// Check if the direction is not close to <see cref="Const.EPSILON">Const.EPSILON</see>
        /// </summary>
        /// <param name="direction"></param>
        /// <returns></returns>
        private bool DirectionNotZero(float direction)
        {
            return direction < -Const.EPSILON || Const.EPSILON < direction;
        }

        /// <summary>
        /// Manage the drowning state of the intern
        /// </summary>
        private void SetFaceUnderwaterFilters()
        {
            if (Npc.isPlayerDead)
            {
                return;
            }
            if (Npc.underwaterCollider != null
                && Npc.underwaterCollider.bounds.Contains(Npc.gameplayCamera.transform.position))
            {
                setFaceUnderwater = true;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 0f, 4f * Time.deltaTime);
                this.drowningTimer -= Time.deltaTime / 10f;
                if (this.drowningTimer < 0f)
                {
                    this.drowningTimer = 1f;
                    Plugin.LogDebug($"SyncKillIntern from drowning for LOCAL client #{Npc.NetworkManager.LocalClientId}, intern object: Intern #{Npc.playerClientId}");
                    InternAIController.SyncKillIntern(Vector3.zero, true, CauseOfDeath.Drowning, 0, default);
                }
            }
            else
            {
                setFaceUnderwater = false;
                Npc.statusEffectAudio.volume = Mathf.Lerp(Npc.statusEffectAudio.volume, 1f, 4f * Time.deltaTime);
                this.drowningTimer = Mathf.Clamp(this.drowningTimer + Time.deltaTime, 0.1f, 1f);
            }

            this.syncUnderwaterInterval -= Time.deltaTime;
            if (this.syncUnderwaterInterval <= 0f)
            {
                this.syncUnderwaterInterval = 0.5f;
                if (setFaceUnderwater && !Npc.isUnderwater)
                {
                    Npc.isUnderwater = true;
                    InternAIController.SyncSetFaceUnderwaterServerRpc(Npc.isUnderwater);
                    return;
                }
                else if (!setFaceUnderwater && Npc.isUnderwater)
                {
                    Npc.isUnderwater = false;
                    InternAIController.SyncSetFaceUnderwaterServerRpc(Npc.isUnderwater);
                    return;
                }
            }
        }

        /// <summary>
        /// Unused for now, can't find the true size of models...
        /// </summary>
        public void RefreshBillBoardPosition()
        {
            if (Plugin.IsModModelReplacementAPILoaded)
            {
                Npc.usernameCanvas.transform.position = GetBillBoardPositionModelReplacementAPI(Npc.usernameCanvas.transform.position);
            }
            else
            {
                Npc.usernameCanvas.transform.position = GetBillBoardPosition(Npc.gameObject, Npc.usernameCanvas.transform.position);
            }
        }

        private Vector3 GetBillBoardPosition(GameObject bodyModel, Vector3 lastPosition)
        {
            // Code from mod ModelReplacementAPI.BodyReplacementBase.GetBounds
            IEnumerable<Bounds> source = from r in bodyModel.GetComponentsInChildren<SkinnedMeshRenderer>()
                                         select r.bounds;
            float yMax = (from x in source
                          orderby x.size.y descending
                          select x).First<Bounds>().size.y;

            Plugin.LogDebug($"skinned y {yMax}");

            foreach (var s in source)
            {
                Plugin.LogDebug($"skinned size {s.size}");

            }

            return new Vector3(lastPosition.x,
                               bodyModel.transform.position.y + yMax,
                               lastPosition.z);
        }

        private Vector3 GetBillBoardPositionModelReplacementAPI(Vector3 lastPosition)
        {
            BodyReplacementBase? bodyReplacement = Npc.gameObject.GetComponent<BodyReplacementBase>();
            if (bodyReplacement == null)
            {
                return GetBillBoardPosition(Npc.gameObject, lastPosition);
            }

            GameObject? model = bodyReplacement.replacementModel;
            if (model == null)
            {
                return GetBillBoardPosition(Npc.gameObject, lastPosition);
            }

            return GetBillBoardPosition(model, Npc.usernameCanvas.transform.position);
        }
    }
}