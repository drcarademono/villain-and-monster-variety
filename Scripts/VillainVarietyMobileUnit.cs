using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Utility.AssetInjection;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility.ModSupport.ModSettings;

namespace VillainVariety
{
    [ImportedComponent]
    public class VillainVarietyMobileUnit : MobileUnit
    {
        static Dictionary<string, Texture2D[][]> textureCache = new Dictionary<string, Texture2D[][]>();
        static Dictionary<string, int> faceCountCache = new Dictionary<string, int>();
        static Dictionary<string, int> outfitCountCache = new Dictionary<string, int>();
        static Dictionary<int, bool> regionalArchivesCache = new Dictionary<int, bool>();

        static Mod mod;
        static bool enemyFaces;
        static bool forceNoNudity;
        static bool forceHelmetSwap;

        static bool usingNinelanNoNudity;
        static bool usingHelmetSwap;
        #region Original Code
        const int numberOrientations = 8;
        const float anglePerOrientation = 360f / numberOrientations;

        Camera mainCamera = null;
        MeshFilter meshFilter = null;
        MeshRenderer meshRenderer = null;

        Vector3 cameraPosition;
        float enemyFacingAngle;
        int lastOrientation;
        int currentFrame;
        int frameIterator;
        bool doMeleeDamage = false;
        bool shootArrow = false;
        bool restartAnims = true;
        bool freezeAnims = false;
        bool animReversed = false;
        int frameSpeedDivisor = 1;

        public override bool IsSetup
        {
            get { return summary.IsSetup; }
            protected set { summary.IsSetup = value; }
        }

        public int CurrentFrame
        {
            get { return currentFrame; }
        }

        public override int FrameSpeedDivisor
        {
            get { return frameSpeedDivisor; }
            set { frameSpeedDivisor = (value < 1) ? 1 : value; }
        }

        public override bool DoMeleeDamage
        {
            get { return doMeleeDamage; }
            set { doMeleeDamage = value; }
        }

        public override bool ShootArrow
        {
            get { return shootArrow; }
            set { shootArrow = value; }
        }

        public override bool FreezeAnims
        {
            get { return freezeAnims; }
            set { freezeAnims = value; }
        }

        public override MobileEnemy Enemy
        {
            get { return summary.Enemy; }
            protected set { summary.Enemy = value; }
        }

        public override MobileStates EnemyState
        {
            get { return summary.EnemyState; }
            protected set { summary.EnemyState = value; }
        }

        public override bool IsBackFacing
        {
            get { return summary.AnimStateRecord % 5 > 2; }
        }

        public override byte ClassicSpawnDistanceType
        {
            get { return summary.ClassicSpawnDistanceType; }
            protected set { summary.ClassicSpawnDistanceType = value; }
        }

        public override bool SpecialTransformationCompleted
        {
            get { return Summary.specialTransformationCompleted; }
            protected set { summary.specialTransformationCompleted = value; }
        }

        void Start()
        {
            if (Application.isPlaying)
            {
                // Get component references
                mainCamera = GameObject.FindGameObjectWithTag("MainCamera").GetComponent<Camera>();
                meshFilter = GetComponent<MeshFilter>();
                meshRenderer = GetComponent<MeshRenderer>();
            }
        }

        void OnDisable()
        {
            restartAnims = true;
        }

        void Update()
        {
            // Restart animation coroutine if not running
            if (restartAnims)
            {
                StartCoroutine(AnimateEnemy());
                restartAnims = false;
            }

            // Rotate to face camera in game
            if (mainCamera && Application.isPlaying)
            {
                // Rotate billboard based on camera facing
                cameraPosition = mainCamera.transform.position;
                Vector3 viewDirection = -new Vector3(mainCamera.transform.forward.x, 0, mainCamera.transform.forward.z);
                transform.LookAt(transform.position + viewDirection);

                // Orient enemy based on camera position
                UpdateOrientation();
            }
        }

        protected override void ApplyEnemy(DaggerfallUnity dfUnity)
        {
            // Load enemy content
            int archive = GetTextureArchive();
            CacheRecordSizesAndFrames(dfUnity, archive);
            AssignMeshAndMaterial(dfUnity, archive);

            // Apply enemy state and update orientation
            lastOrientation = -1;
            ApplyEnemyState();
        }

        protected override void ApplyEnemyStateChange(MobileStates currentState, MobileStates newState)
        {
            // Don't reset frame to 0 for idle/move switches for enemies without idle animations
            bool resetFrame = true;

            if (!summary.Enemy.HasIdle &&
                ((currentState == MobileStates.Idle && newState == MobileStates.Move) ||
                (currentState == MobileStates.Move && newState == MobileStates.Idle)))
                resetFrame = false;

            if (resetFrame)
            {
                currentFrame = 0;
                animReversed = false;
            }

            ApplyEnemyState();
        }

        public override Vector3 GetSize()
        {
            return summary.RecordSizes[0];
        }

        #region Private Methods

        /// <summary>
        /// Updates enemy state based on current settings.
        /// Called automatially by SetEnemyType().
        /// This should be called after changing enemy state (e.g. from in code or in editor).
        /// </summary>
        private void ApplyEnemyState()
        {
            // Get state animations
            summary.StateAnims = GetStateAnims(summary.EnemyState);
            if (summary.EnemyState == MobileStates.PrimaryAttack)
            {
                int random = Dice100.Roll();

                if (random <= summary.Enemy.ChanceForAttack2)
                    summary.StateAnimFrames = summary.Enemy.PrimaryAttackAnimFrames2;
                else
                {
                    random -= summary.Enemy.ChanceForAttack2;
                    if (random <= summary.Enemy.ChanceForAttack3)
                        summary.StateAnimFrames = summary.Enemy.PrimaryAttackAnimFrames3;
                    else
                    {
                        random -= summary.Enemy.ChanceForAttack3;
                        if (random <= summary.Enemy.ChanceForAttack4)
                            summary.StateAnimFrames = summary.Enemy.PrimaryAttackAnimFrames4;
                        else
                        {
                            random -= summary.Enemy.ChanceForAttack4;
                            if (random <= summary.Enemy.ChanceForAttack5)
                                summary.StateAnimFrames = summary.Enemy.PrimaryAttackAnimFrames5;
                            else
                                summary.StateAnimFrames = summary.Enemy.PrimaryAttackAnimFrames;
                        }
                    }
                }

                // Set to the first frame of this animation, and prepare frameIterator to start from the second frame when AnimateEnemy() next runs
                currentFrame = summary.StateAnimFrames[0];
                frameIterator = 1;
            }

            if (summary.EnemyState == MobileStates.RangedAttack1 || summary.EnemyState == MobileStates.RangedAttack2)
            {
                summary.StateAnimFrames = summary.Enemy.RangedAttackAnimFrames;

                // Set to the first frame of this animation, and prepare frameIterator to start from the second frame when AnimateEnemy() next runs
                currentFrame = summary.StateAnimFrames[0];
                frameIterator = 1;
            }

            if (summary.EnemyState == MobileStates.Spell)
            {
                summary.StateAnimFrames = summary.Enemy.SpellAnimFrames;

                // Set to the first frame of this animation, and prepare frameIterator to start from the second frame when AnimateEnemy() next runs
                currentFrame = summary.StateAnimFrames[0];
                frameIterator = 1;
            }

            if (summary.EnemyState == MobileStates.SeducerTransform1)
            {
                // Switch to flying sprite alignment while crouched and growing wings
                summary.Enemy.Behaviour = MobileBehaviour.Flying;
                summary.StateAnimFrames = summary.Enemy.SeducerTransform1Frames;

                // Set to the first frame of this animation, and prepare frameIterator to start from the second frame when AnimateEnemy() next runs
                currentFrame = summary.StateAnimFrames[0];
                frameIterator = 1;
            }

            if (summary.EnemyState == MobileStates.SeducerTransform2)
            {
                // Switch to grounded sprite alignment while standing and spreading wings
                summary.Enemy.Behaviour = MobileBehaviour.General;
                summary.StateAnimFrames = summary.Enemy.SeducerTransform2Frames;

                // Set to the first frame of this animation, and prepare frameIterator to start from the second frame when AnimateEnemy() next runs
                currentFrame = summary.StateAnimFrames[0];
                frameIterator = 1;
            }

            if (summary.StateAnims == null)
            {
                // Log error message
                DaggerfallUnity.LogMessage(string.Format("DaggerfalMobileUnit: Enemy does not have animation for {0} state. Defaulting to Idle state.", summary.EnemyState.ToString()), true);

                // Set back to idle (which every enemy has in one form or another)
                summary.EnemyState = MobileStates.Idle;
                summary.StateAnims = GetStateAnims(summary.EnemyState);
            }

            // One of the frost daedra's sets of attack frames starts with the hit frame (-1), so we need to check for that right away before updating orientation.
            if (currentFrame == -1 && summary.EnemyState == MobileStates.PrimaryAttack)
            {
                doMeleeDamage = true;
                if (frameIterator < summary.StateAnimFrames.Length)
                    currentFrame = summary.StateAnimFrames[frameIterator++];
            }

            // Orient enemy relative to camera
            UpdateOrientation();
        }

        /// <summary>
        /// Updates enemy orientation based on angle between enemy and camera.
        /// </summary>
        private void UpdateOrientation()
        {
            Transform parent = transform.parent;
            if (parent == null)
                return;

            // Get direction normal to camera, ignore y axis
            Vector3 dir = Vector3.Normalize(
                new Vector3(cameraPosition.x, 0, cameraPosition.z) -
                new Vector3(transform.position.x, 0, transform.position.z));

            // Get parent forward normal, ignore y axis
            Vector3 parentForward = transform.parent.forward;
            parentForward.y = 0;

            // Get angle and cross product for left/right angle
            enemyFacingAngle = Vector3.Angle(dir, parentForward);
            enemyFacingAngle = enemyFacingAngle * -Mathf.Sign(Vector3.Cross(dir, parentForward).y);

            // Facing index
            int orientation = -Mathf.RoundToInt(enemyFacingAngle / anglePerOrientation);
            // Wrap values to 0 .. numberOrientations-1
            orientation = (orientation + numberOrientations) % numberOrientations;

            // Change enemy to this orientation
            if (orientation != lastOrientation)
                OrientEnemy(orientation);
        }

        /// <summary>
        /// Sets enemy orientation index.
        /// </summary>
        /// <param name="orientation">New orientation index.</param>
        private void OrientEnemy(int orientation)
        {
            if (summary.StateAnims == null || summary.StateAnims.Length == 0)
                return;

            // Get mesh filter
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();

            // Try to fix if anim array is null
            if (summary.StateAnims == null)
                ApplyEnemyState();

            // Get enemy size and scale for this state
            int record = summary.StateAnims[orientation].Record;
            summary.AnimStateRecord = record;
            Vector2 size = summary.RecordSizes[record];

            // Post-fix female texture scale for 475 while casting spells
            // The scale read from Daggerfall's files is too small 
            if (summary.Enemy.FemaleTexture == 475 &&
                summary.Enemy.Gender == MobileGender.Female &&
                record >= 20 && record <= 24)
            {
                size *= 1.35f;
            }

            // Ensure walking enemies keep their feet aligned between states
            if (summary.Enemy.Behaviour != MobileBehaviour.Flying &&
                summary.Enemy.Behaviour != MobileBehaviour.Aquatic &&
                summary.EnemyState != MobileStates.Idle)
            {
                Vector2 idleSize = summary.RecordSizes[0];
                transform.localPosition = new Vector3(0f, (size.y - idleSize.y) * 0.5f, 0f);
            }
            else
            {
                transform.localPosition = Vector3.zero;
            }

            // Set mesh scale for this state
            transform.localScale = new Vector3(size.x, size.y, 1);

            // Check if orientation flip needed
            bool flip = summary.StateAnims[orientation].FlipLeftRight;

            // Scorpion animations need to be inverted
            if (summary.Enemy.ID == (int)MobileTypes.GiantScorpion)
                flip = !flip;

            // Update Record/Frame texture
            if (summary.ImportedTextures.HasImportedTextures)
            {
                if (meshRenderer == null)
                    meshRenderer = GetComponent<MeshRenderer>();

                // Assign imported texture
                meshRenderer.material.mainTexture = summary.ImportedTextures.Albedo[record][currentFrame];
                if (summary.ImportedTextures.IsEmissive)
                    meshRenderer.material.SetTexture("_EmissionMap", summary.ImportedTextures.EmissionMaps[record][currentFrame]);

                // Update UVs on mesh
                Vector2[] uvs = new Vector2[4];
                if (flip)
                {
                    uvs[0] = new Vector2(1, 1);
                    uvs[1] = new Vector2(0, 1);
                    uvs[2] = new Vector2(1, 0);
                    uvs[3] = new Vector2(0, 0);
                }
                else
                {
                    uvs[0] = new Vector2(0, 1);
                    uvs[1] = new Vector2(1, 1);
                    uvs[2] = new Vector2(0, 0);
                    uvs[3] = new Vector2(1, 0);
                }
                meshFilter.sharedMesh.uv = uvs;
            }
            else
            {
                // Daggerfall Atlas: Update UVs on mesh
                Rect rect = summary.AtlasRects[summary.AtlasIndices[record].startIndex + currentFrame];
                Vector2[] uvs = new Vector2[4];
                if (flip)
                {
                    uvs[0] = new Vector2(rect.xMax, rect.yMax);
                    uvs[1] = new Vector2(rect.x, rect.yMax);
                    uvs[2] = new Vector2(rect.xMax, rect.y);
                    uvs[3] = new Vector2(rect.x, rect.y);
                }
                else
                {
                    uvs[0] = new Vector2(rect.x, rect.yMax);
                    uvs[1] = new Vector2(rect.xMax, rect.yMax);
                    uvs[2] = new Vector2(rect.x, rect.y);
                    uvs[3] = new Vector2(rect.xMax, rect.y);
                }
                meshFilter.sharedMesh.uv = uvs;
            }

            // Assign new orientation
            lastOrientation = orientation;
        }

        IEnumerator AnimateEnemy()
        {
            float fps = 10;
            while (true)
            {
                if (!freezeAnims && summary.IsSetup && summary.StateAnims != null && summary.StateAnims.Length > 0)
                {
                    // Update enemy and fps
                    OrientEnemy(lastOrientation);
                    fps = summary.StateAnims[lastOrientation].FramePerSecond / FrameSpeedDivisor;

                    // Enforce a lower limit on animation speed when using a custom FrameSpeedDivisor
                    if (FrameSpeedDivisor > 1 && fps < 4)
                        fps = 4;

                    bool doingAttackAnimation = (summary.EnemyState == MobileStates.PrimaryAttack ||
                        summary.EnemyState == MobileStates.RangedAttack1 ||
                        summary.EnemyState == MobileStates.RangedAttack2);

                    // Reset to idle if frameIterator has finished. Used for attack animations.
                    if (doingAttackAnimation && frameIterator >= summary.StateAnimFrames.Length)
                    {
                        ChangeEnemyState(MobileStates.Idle);
                        frameIterator = 0;
                    }

                    // Step frame
                    if (!doingAttackAnimation)
                        currentFrame = (animReversed) ? currentFrame - 1 : currentFrame + 1;
                    else // Attack animation
                    {
                        currentFrame = summary.StateAnimFrames[frameIterator++];

                        // Set boolean if frame to attempt to apply damage or shoot arrow is encountered, and proceed to next frame if it exists
                        if (currentFrame == -1)
                        {
                            if (summary.EnemyState == MobileStates.PrimaryAttack)
                                doMeleeDamage = true;
                            else if (summary.EnemyState == MobileStates.RangedAttack1 || summary.EnemyState == MobileStates.RangedAttack2)
                                shootArrow = true;

                            if (frameIterator < summary.StateAnimFrames.Length)
                                currentFrame = summary.StateAnimFrames[frameIterator++];
                        }
                    }

                    if (currentFrame >= summary.StateAnims[lastOrientation].NumFrames ||
                        animReversed && currentFrame <= 0)
                    {
                        if (IsPlayingOneShot())
                            ChangeEnemyState(NextStateAfterCurrentOneShot());   // If this is a one-shot anim, revert to next state (usually idle)
                        else
                        {
                            // Otherwise keep looping frames
                            bool bounceAnim = summary.StateAnims[lastOrientation].BounceAnim;
                            if (bounceAnim && !animReversed)
                            {
                                currentFrame = summary.StateAnims[lastOrientation].NumFrames - 2;
                                animReversed = true;
                            }
                            else
                            {
                                currentFrame = 0;
                                animReversed = false;
                            }
                        }
                    }
                }

                yield return new WaitForSeconds(1f / fps);
            }
        }

        /// <summary>
        /// Gets the next state after finished playing current oneshot state
        /// </summary>
        /// <returns>Next state.</returns>
        MobileStates NextStateAfterCurrentOneShot()
        {
            switch (summary.EnemyState)
            {
                case MobileStates.SeducerTransform1:
                    return MobileStates.SeducerTransform2;
                case MobileStates.SeducerTransform2:
                    SetSpecialTransformationCompleted();
                    return MobileStates.Idle;
                default:
                    return MobileStates.Idle;
            }
        }

        #endregion

        #region Content Loading

        /// <summary>
        /// Get texture archive index based on gender.
        /// Assigns random gender for humans enemies with unspecified gender.
        /// </summary>
        /// <returns>Texture archive index.</returns>
        private int GetTextureArchive()
        {
            // If human with unspecified gender then randomise gender
            if (summary.Enemy.Affinity == MobileAffinity.Human && summary.Enemy.Gender == MobileGender.Unspecified)
            {
                if (DFRandom.random_range(0, 2) == 0)
                    summary.Enemy.Gender = MobileGender.Male;
                else
                    summary.Enemy.Gender = MobileGender.Female;
            }

            // Monster genders are always unspecified as there is no male/female variant
            if (summary.Enemy.Gender == MobileGender.Male || summary.Enemy.Gender == MobileGender.Unspecified)
                return summary.Enemy.MaleTexture;
            else
                return summary.Enemy.FemaleTexture;
        }

        /// <summary>
        /// Precalculate and cache billboard scale for every record.
        /// This will change based on animation state and orientation.
        /// Cache this to array so it only needs to be calculated once.
        /// Also store number of frames for state animations.
        /// </summary>
        /// <param name="dfUnity">DaggerfallUnity singleton. Required for content readers and settings.</param>
        /// <param name="archive">Texture archive index derived from type and gender.</param>
        private void CacheRecordSizesAndFrames(DaggerfallUnity dfUnity, int archive)
        {
            // Open texture file
            string path = Path.Combine(dfUnity.Arena2Path, TextureFile.IndexToFileName(archive));
            TextureFile textureFile = new TextureFile();

            // Might be updated later through texture replacement
            if (!textureFile.Load(path, FileUsage.UseMemory, true))
                return;

            // Cache size and scale for each record
            summary.RecordSizes = new Vector2[textureFile.RecordCount];
            summary.RecordFrames = new int[textureFile.RecordCount];
            for (int i = 0; i < textureFile.RecordCount; i++)
            {
                // Get size and scale of this texture
                DFSize size = textureFile.GetSize(i);
                DFSize scale = textureFile.GetScale(i);

                // Set start size
                Vector2 startSize;
                startSize.x = size.Width;
                startSize.y = size.Height;

                // Apply scale
                Vector2 finalSize;
                int xChange = (int)(size.Width * (scale.Width / BlocksFile.ScaleDivisor));
                int yChange = (int)(size.Height * (scale.Height / BlocksFile.ScaleDivisor));
                finalSize.x = (size.Width + xChange);
                finalSize.y = (size.Height + yChange);

                // Set optional scale
                TextureReplacement.SetBillboardScale(archive, i, ref finalSize);

                // Store final size and frame count
                summary.RecordSizes[i] = finalSize * MeshReader.GlobalScale;
                summary.RecordFrames[i] = textureFile.GetFrameCount(i);
            }
        }

        /// <summary>
        /// Creates mesh and material for this enemy.
        /// </summary>
        /// <param name="dfUnity">DaggerfallUnity singleton. Required for content readers and settings.</param>
        /// <param name="archive">Texture archive index derived from type and gender.</param>
        private void AssignMeshAndMaterial(DaggerfallUnity dfUnity, int archive)
        {
            // Get mesh filter
            if (meshFilter == null)
                meshFilter = GetComponent<MeshFilter>();

            // Vertices for a 1x1 unit quad
            // This is scaled to correct size depending on facing and orientation
            float hx = 0.5f, hy = 0.5f;
            Vector3[] vertices = new Vector3[4];
            vertices[0] = new Vector3(hx, hy, 0);
            vertices[1] = new Vector3(-hx, hy, 0);
            vertices[2] = new Vector3(hx, -hy, 0);
            vertices[3] = new Vector3(-hx, -hy, 0);

            // Indices
            int[] indices = new int[6]
                {
                    0, 1, 2,
                    3, 2, 1,
                };

            // Normals
            Vector3 normal = Vector3.Normalize(Vector3.up + Vector3.forward);
            Vector3[] normals = new Vector3[4];
            normals[0] = normal;
            normals[1] = normal;
            normals[2] = normal;
            normals[3] = normal;

            // Create mesh
            Mesh mesh = new Mesh();
            mesh.name = string.Format("MobileEnemyMesh");
            mesh.vertices = vertices;
            mesh.triangles = indices;
            mesh.normals = normals;

            // Assign mesh
            meshFilter.sharedMesh = mesh;

            // Create material
            Material material = LoadVillainVariant(archive, meshFilter, ref summary.ImportedTextures) ??
                TextureReplacement.GetMobileBillboardMaterial(archive, GetComponent<MeshFilter>(), ref summary.ImportedTextures) ??
                dfUnity.MaterialReader.GetMaterialAtlas(
                archive,
                0,
                4,
                1024,
                out summary.AtlasRects,
                out summary.AtlasIndices,
                4,
                true,
                0,
                false,
                true);

            // Update cached record values in case of non-classic texture
            if (summary.RecordSizes == null || summary.RecordSizes.Length == 0)
            {
                if (summary.ImportedTextures.Albedo != null && summary.ImportedTextures.Albedo.Length > 0)
                {
                    int recordCount = summary.ImportedTextures.Albedo.Length;

                    // Cache size and scale for each record
                    summary.RecordSizes = new Vector2[recordCount];
                    summary.RecordFrames = new int[recordCount];
                    for (int i = 0; i < recordCount; i++)
                    {
                        // Get size and scale of this texture
                        Texture2D firstFrame = summary.ImportedTextures.Albedo[i][0];

                        Vector2 size = new Vector2(firstFrame.width, firstFrame.height);

                        // Set optional scale
                        TextureReplacement.SetBillboardScale(archive, i, ref size);

                        // Store final size and frame count
                        summary.RecordSizes[i] = size * MeshReader.GlobalScale;
                        summary.RecordFrames[i] = summary.ImportedTextures.Albedo[i].Length;
                    }
                }
                else
                {
                    Debug.LogError($"Texture archive {archive} has no valid records");
                }
            }

            // Set new enemy material
            GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// Gets cloned animation set for specified state.
        /// </summary>
        /// <param name="state">Enemy state.</param>
        /// <returns>Array of mobile animations.</returns>
        private MobileAnimation[] GetStateAnims(MobileStates state)
        {
            // Clone static animation state
            MobileAnimation[] anims;
            switch (state)
            {
                case MobileStates.Move:
                    if ((MobileTypes)summary.Enemy.ID == MobileTypes.Ghost ||
                        (MobileTypes)summary.Enemy.ID == MobileTypes.Wraith)
                        anims = (MobileAnimation[])EnemyBasics.GhostWraithMoveAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.DaedraSeducer && summary.specialTransformationCompleted)
                        anims = (MobileAnimation[])EnemyBasics.SeducerIdleMoveAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.Slaughterfish)
                        anims = (MobileAnimation[])EnemyBasics.SlaughterfishMoveAnims.Clone();
                    else
                        anims = (MobileAnimation[])EnemyBasics.MoveAnims.Clone();
                    break;
                case MobileStates.PrimaryAttack:
                    if ((MobileTypes)summary.Enemy.ID == MobileTypes.Ghost ||
                        (MobileTypes)summary.Enemy.ID == MobileTypes.Wraith)
                        anims = (MobileAnimation[])EnemyBasics.GhostWraithAttackAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.DaedraSeducer && summary.specialTransformationCompleted)
                        anims = (MobileAnimation[])EnemyBasics.SeducerAttackAnims.Clone();
                    else
                        anims = (MobileAnimation[])EnemyBasics.PrimaryAttackAnims.Clone();
                    break;
                case MobileStates.Hurt:
                    if ((MobileTypes)summary.Enemy.ID == MobileTypes.DaedraSeducer && summary.specialTransformationCompleted)
                        anims = (MobileAnimation[])EnemyBasics.SeducerIdleMoveAnims.Clone();
                    else
                        anims = (MobileAnimation[])EnemyBasics.HurtAnims.Clone();
                    break;
                case MobileStates.Idle:
                    if ((MobileTypes)summary.Enemy.ID == MobileTypes.Ghost ||
                        (MobileTypes)summary.Enemy.ID == MobileTypes.Wraith)
                        anims = (MobileAnimation[])EnemyBasics.GhostWraithMoveAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.DaedraSeducer && summary.specialTransformationCompleted)
                        anims = (MobileAnimation[])EnemyBasics.SeducerIdleMoveAnims.Clone();
                    else if (summary.Enemy.FemaleTexture == 483 &&
                        summary.Enemy.Gender == MobileGender.Female)
                        anims = (MobileAnimation[])EnemyBasics.FemaleThiefIdleAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.Rat)
                        anims = (MobileAnimation[])EnemyBasics.RatIdleAnims.Clone();
                    else if ((MobileTypes)summary.Enemy.ID == MobileTypes.Slaughterfish)
                        anims = (MobileAnimation[])EnemyBasics.SlaughterfishMoveAnims.Clone();
                    else if (!summary.Enemy.HasIdle)
                        anims = (MobileAnimation[])EnemyBasics.MoveAnims.Clone();
                    else
                        anims = (MobileAnimation[])EnemyBasics.IdleAnims.Clone();
                    break;
                case MobileStates.RangedAttack1:
                    anims = (summary.Enemy.HasRangedAttack1) ? (MobileAnimation[])EnemyBasics.RangedAttack1Anims.Clone() : null;
                    break;
                case MobileStates.RangedAttack2:
                    anims = (summary.Enemy.HasRangedAttack2) ? (MobileAnimation[])EnemyBasics.RangedAttack2Anims.Clone() : null;
                    break;
                case MobileStates.Spell:
                    if ((MobileTypes)summary.Enemy.ID == MobileTypes.DaedraSeducer && summary.specialTransformationCompleted)
                        anims = (MobileAnimation[])EnemyBasics.SeducerAttackAnims.Clone();
                    else
                        anims = (summary.Enemy.HasSpellAnimation) ? (MobileAnimation[])EnemyBasics.RangedAttack1Anims.Clone() : (MobileAnimation[])EnemyBasics.PrimaryAttackAnims.Clone();
                    break;
                case MobileStates.SeducerTransform1:
                    anims = (summary.Enemy.HasSeducerTransform1) ? (MobileAnimation[])EnemyBasics.SeducerTransform1Anims.Clone() : null;
                    break;
                case MobileStates.SeducerTransform2:
                    anims = (summary.Enemy.HasSeducerTransform2) ? (MobileAnimation[])EnemyBasics.SeducerTransform2Anims.Clone() : null;
                    break;
                default:
                    return null;
            }

            // Assign number of frames per anim
            for (int i = 0; i < anims.Length; i++)
                anims[i].NumFrames = summary.RecordFrames[anims[i].Record];

            // If flying, set to faster flying animation speed
            if ((state == MobileStates.Move || state == MobileStates.Idle) && summary.Enemy.Behaviour == MobileBehaviour.Flying)
                for (int i = 0; i < anims.Length; i++)
                    anims[i].FramePerSecond = EnemyBasics.FlyAnimSpeed;

            return anims;
        }

        #endregion

#if UNITY_EDITOR
        /// <summary>
        /// Rotate enemy to face editor camera while game not running.
        /// </summary>
        public void OnDrawGizmos()
        {
            if (!Application.isPlaying)
            {
                // Rotate to face camera
                UnityEditor.SceneView sceneView = GetActiveSceneView();
                if (sceneView)
                {
                    // Editor camera stands in for player camera in edit mode
                    cameraPosition = sceneView.camera.transform.position;
                    Vector3 viewDirection = -new Vector3(sceneView.camera.transform.forward.x, 0, sceneView.camera.transform.forward.z);
                    transform.LookAt(transform.position + viewDirection);
                    UpdateOrientation();
                }
            }
        }

        private SceneView GetActiveSceneView()
        {
            // Return the focused window if it is a SceneView
            if (EditorWindow.focusedWindow != null && EditorWindow.focusedWindow.GetType() == typeof(SceneView))
                return (SceneView)EditorWindow.focusedWindow;

            return null;
        }
#endif
        #endregion

        [Invoke(StateManager.StateTypes.Start, 0)]
        public static void Init(InitParams initParams)
        {
            mod = initParams.Mod;

            mod.LoadSettingsCallback = LoadSettings;
            mod.LoadSettings();
            mod.IsReady = true;
        }

        static void LoadSettings(ModSettings modSettings, ModSettingsChange change)
        {
            enemyFaces = modSettings.GetBool("Core", "EnemyFaces");
            forceNoNudity = modSettings.GetBool("Compatibility", "ForceNoNudity");
            forceHelmetSwap = modSettings.GetBool("Compatibility", "ForceHelmetSwap");
        }

        static bool HasHelmetSwap()
        {
            Mod helmetSwap = ModManager.Instance.GetMod("Helmet Swap");
            return helmetSwap != null && helmetSwap.Enabled;
        }

        static bool HasNoNudity()
        {
            Mod noNudity = ModManager.Instance.GetMod("No nudity");
            return noNudity != null && noNudity.Enabled;
        }

        private static bool IsInHammerfell()
        {
            var GPS = GameManager.Instance.PlayerGPS;
            switch ((MapsFile.Climates)GPS.CurrentClimateIndex)
            {
                case MapsFile.Climates.Desert:
                case MapsFile.Climates.Desert2:
                case MapsFile.Climates.Swamp:
                case MapsFile.Climates.Rainforest:
                case MapsFile.Climates.Subtropical:
                    return true;
            }

            switch (GPS.CurrentRegionIndex)
            {
                case 1: // Dragontail Mountains
                case 53: // Ephesus
                    return true;
            }

            return false;
        }

        string GetSettingPrefix(int archive)
        {
            switch(archive)
            {
                case 265: // Nymph
                case 284: // Seducer
                case 297: // Lamia
                    if (forceNoNudity || HasNoNudity())
                        return ".NN";
                    break;

                case 478: // Hammer Knight Male
                case 481: // Light Fighter Female 
                case 482: // Light Fighter Male
                case 487: // Axe Knight Female
                    if (forceHelmetSwap || HasHelmetSwap())
                        return ".HS";
                    break;
            }

            return "";
        }

        string GetRegionPrefix()
        {
            return IsInHammerfell() ? "RG" : "BN";
        }

        string GetRegionalImageName(int archive, int record, int frame, int face, string setting, int outfitIndex)
        {
            if (outfitIndex == 0)
            {
                return GetRegionalImageName(archive, record, frame, face, setting, "");
            }
            else
            {
                string outfit = "." + GetRegionPrefix() + outfitIndex.ToString();
                return GetRegionalImageName(archive, record, frame, face, setting, outfit);
            }
        }

        string GetRegionalImageName(int archive, int record, int frame, int face, string setting, string outfit)
        {
            return string.Format("{0:000}.{3}{4}{5}_{1}-{2}", archive, record, frame, face, setting, outfit);
        }

        string GetImageName(int archive, int record, int frame, int face, string setting, int outfitIndex)
        {
            if(outfitIndex == 0)
            {
                return string.Format("{0:000}.{3}.{4}_{1}-{2}", archive, record, frame, face, setting);
            }
            else
            {
                return string.Format("{0:000}.{3}{4}.{5}_{1}-{2}", archive, record, frame, face, setting, outfitIndex);
            }
        }

        string GetRegionalImageName(int archive, int record, int frame, string setting, int outfitIndex)
        {
            if (outfitIndex == 0)
            {
                return GetRegionalImageName(archive, record, frame, setting, "");
            }
            else
            {
                string outfit = "." + GetRegionPrefix() + outfitIndex.ToString();
                return GetRegionalImageName(archive, record, frame, setting, outfit);
            }
        }

        string GetRegionalImageName(int archive, int record, int frame, string setting, string outfit)
        {
            return string.Format("{0:000}.X{3}{4}_{1}-{2}", archive, record, frame, setting, outfit);
        }

        string GetImageName(int archive, int record, int frame, string setting, int outfitIndex)
        {
            if (outfitIndex == 0)
            {
                return string.Format("{0:000}.X{3}_{1}-{2}", archive, record, frame, setting);
            }
            else
            {
                return string.Format("{0:000}.X{3}.{4}_{1}-{2}", archive, record, frame, setting, outfitIndex);
            }
        }

        string GetRegionalImageName(int archive, int record, int frame, int? face, string setting, int outfitIndex)
        {
            if (face.HasValue)
            {
                return GetRegionalImageName(archive, record, frame, face.Value, setting, outfitIndex);
            }
            else
            {
                return GetRegionalImageName(archive, record, frame, setting, outfitIndex);
            }
        }

        string GetImageName(int archive, int record, int frame, int? face, string setting, int outfitIndex)
        {
            if (face.HasValue)
            {
                return GetImageName(archive, record, frame, face.Value, setting, outfitIndex);
            }
            else
            {
                return GetImageName(archive, record, frame, setting, outfitIndex);
            }
        }

        bool IsRegional(int archive, string setting)
        {
            if (!regionalArchivesCache.TryGetValue(archive, out bool regional))
            {
                regional = ModManager.Instance.TryGetAsset(GetRegionalImageName(archive, 0, 0, 1, setting, 1), clone: false, out Texture2D _);
                regionalArchivesCache.Add(archive, regional);
            }
            return regional;
        }

        const string villagerVarietyMod = "Vanilla Enhanced - Villagers";
        const string villagerVarietyGetArchivePrexix = "getArchivePrefix";

        Material LoadGuardVariant(int archive, MeshFilter meshfilter, ref MobileBillboardImportedTextures importedTextures)
        {
            Mod mod = ModManager.Instance.GetMod(villagerVarietyMod);
            if (mod == null || !mod.Enabled)
                return null;

            string prefix = null;
            ModManager.Instance.SendModMessage(villagerVarietyMod, villagerVarietyGetArchivePrexix, archive,
                (_, data) =>
            {
                prefix = data as string;
            });

            if (string.IsNullOrEmpty(prefix))
                return null;

            string firstFrameName = $"{prefix}0-0";

            if (!textureCache.TryGetValue(firstFrameName, out importedTextures.Albedo))
            {
                string classicFilename = TextureFile.IndexToFileName(archive);
                TextureFile textureFile = new TextureFile();
                if (!textureFile.Load(Path.Combine(DaggerfallUnity.Instance.Arena2Path, classicFilename), FileUsage.UseMemory, true))
                {
                    Debug.LogErrorFormat("Villain Variant: archive {0} not supported", archive);
                    return null;
                }

                importedTextures.Albedo = new Texture2D[textureFile.RecordCount][];
                for (int record = 0; record < textureFile.RecordCount; ++record)
                {
                    var frameCount = textureFile.GetFrameCount(record);
                    importedTextures.Albedo[record] = new Texture2D[frameCount];

                    for (int frame = 0; frame < frameCount; ++frame)
                    {
                        Texture2D frameAsset;
                        string frameFilename = $"{prefix}{record}-{frame}";
                        if(!ModManager.Instance.TryGetAsset(frameFilename, clone: false, out frameAsset))
                        {
                            frameAsset = ImageReader.GetTexture(classicFilename, record, frame, hasAlpha: true);
                        }
                        importedTextures.Albedo[record][frame] = frameAsset;
                    }
                }

                textureCache[firstFrameName] = importedTextures.Albedo;
            }

            SetUv(meshFilter);
            importedTextures.HasImportedTextures = true;

            return MaterialReader.CreateBillboardMaterial();
        }

        Material LoadVillainVariant(int archive, MeshFilter meshFilter, ref MobileBillboardImportedTextures importedTextures)
        {
            if (archive == 399)
                return LoadGuardVariant(archive, meshFilter, ref importedTextures);

            string setting = GetSettingPrefix(archive);
            int face = enemyFaces ? SelectFace(archive, setting) : 0;
            int outfit = SelectOutfit(archive, setting);

            // If we have no V&MV feature at all, return the default
            if (face == 0 && outfit == 0 && string.IsNullOrEmpty(setting))
                return null;

            bool regional = IsRegional(archive, setting);
            string firstFrameName;
            int? usedFace;
            if (face != 0)
            {
                usedFace = face;
                firstFrameName = regional ? GetRegionalImageName(archive, 0, 0, face, setting, outfit) : GetImageName(archive, 0, 0, face, setting, outfit);
            }
            else
            {
                usedFace = null;
                firstFrameName = regional ? GetRegionalImageName(archive, 0, 0, setting, outfit) : GetImageName(archive, 0, 0, setting, outfit);
            }

            if (!textureCache.TryGetValue(firstFrameName, out importedTextures.Albedo))
            {                
                if (usedFace.HasValue && !ModManager.Instance.TryGetAsset(firstFrameName, clone: false, out Texture2D _))
                {
                    // Fallback to default face
                    usedFace = null;
                }
                                
                Texture2D LoadFrame(int record, int frame)
                {
                    Texture2D frameAsset = null;

                    if (regional)
                    {
                        string frameFilename = GetRegionalImageName(archive, record, frame, usedFace, setting, outfit);
                        ModManager.Instance.TryGetAsset(frameFilename, clone: false, out frameAsset);

                        // Try regional default face
                        if (frameAsset == null && usedFace.HasValue)
                        {
                            frameFilename = GetRegionalImageName(archive, record, frame, setting, outfit);
                            ModManager.Instance.TryGetAsset(frameFilename, clone: false, out frameAsset);
                        }
                    }

                    // If not regional or regional was missing
                    if (frameAsset == null)
                    {
                        string frameFilename = GetImageName(archive, record, frame, usedFace, setting, outfit);
                        ModManager.Instance.TryGetAsset(frameFilename, clone: false, out frameAsset);
                    }

                    // Fallback on default face, if we haven't tried that one already
                    if (frameAsset == null && usedFace.HasValue)
                    {
                        string frameFilename = GetImageName(archive, record, frame, setting, outfit);
                        ModManager.Instance.TryGetAsset(frameFilename, clone: false, out frameAsset);
                    }

                    return frameAsset;
                }

                // Check if classic archive
                string classicFilename = TextureFile.IndexToFileName(archive);
                TextureFile textureFile = new TextureFile();
                if (textureFile.Load(Path.Combine(DaggerfallUnity.Instance.Arena2Path, classicFilename), FileUsage.UseMemory, true))
                {
                    importedTextures.Albedo = new Texture2D[textureFile.RecordCount][];
                    for (int record = 0; record < textureFile.RecordCount; ++record)
                    {
                        var frameCount = textureFile.GetFrameCount(record);
                        importedTextures.Albedo[record] = new Texture2D[frameCount];

                        for (int frame = 0; frame < frameCount; ++frame)
                        {
                            Texture2D frameAsset = LoadFrame(record, frame);

                            // Fallback on classic
                            if (frameAsset == null)
                            {
                                frameAsset = ImageReader.GetTexture(classicFilename, record, frame, hasAlpha: true);
                            }

                            importedTextures.Albedo[record][frame] = frameAsset;
                        }
                    }
                }
                else
                {
                    List<Texture2D[]> allAlbedo = new List<Texture2D[]>();

                    bool TryImportTexture(int record, out Texture2D[] frames)
                    {
                        int frame = 0;
                        Texture2D tex;

                        var textures = new List<Texture2D>();

                        while (tex = LoadFrame(record, frame))
                        {
                            textures.Add(tex);
                            frame++;
                        }

                        frames = textures.ToArray();
                        return textures.Count > 0;
                    }

                    int currentRecord = 0;
                    while(TryImportTexture(currentRecord, out Texture2D[] frames))
                    {
                        allAlbedo.Add(frames);
                        currentRecord++;
                    }

                    if(currentRecord == 0)
                    {
                        Debug.LogError($"Villain Variety: couldn't load archive {archive}");
                        return null;
                    }

                    importedTextures.Albedo = allAlbedo.ToArray();
                }

                textureCache[firstFrameName] = importedTextures.Albedo;
            }

            SetUv(meshFilter);
            importedTextures.HasImportedTextures = true;

            return MaterialReader.CreateBillboardMaterial();
        }

        int GetFaceCount(int archive, string setting)
        {
            bool regional = IsRegional(archive, setting);
            string archiveKey = regional ? GetRegionPrefix() + archive.ToString() : archive.ToString();
            if (faceCountCache.TryGetValue(archiveKey, out int count))
                return count;

            count = 0;

            // Faces and outfits are 1-indexed
            if (regional)
            {
                for (; ModManager.Instance.TryGetAsset(GetRegionalImageName(archive, record: 0, frame: 0, face: count + 1, setting, outfitIndex: 1), clone: false, out Texture2D _); count++) ;
            }
            else
            {
                for (; ModManager.Instance.TryGetAsset(GetImageName(archive, record: 0, frame: 0, face: count + 1, setting, outfitIndex: 1), clone: false, out Texture2D _); count++) ;
            }

            faceCountCache.Add(archiveKey, count);
            return count;
        }

        int SelectFace(int archive, string setting)
        {
            int faceCount = GetFaceCount(archive, setting);
            if (faceCount == 0)
                return 0;

            return UnityEngine.Random.Range(0, faceCount) + 1;
        }

        int GetOutfitCount(int archive, string setting)
        {
            bool regional = IsRegional(archive, setting);
            string archiveKey = regional ? GetRegionPrefix() + archive.ToString() : archive.ToString();

            if (outfitCountCache.TryGetValue(archiveKey, out int count))
                return count;

            count = 0;

            if (regional)
            {
                for (; ModManager.Instance.TryGetAsset(GetRegionalImageName(archive, record: 0, frame: 0, face: 1, setting, outfitIndex: count + 1), clone: false, out Texture2D _); count++) ;
            }
            else
            {
                // Faces and outfits are 1-indexed
                for (; ModManager.Instance.TryGetAsset(GetImageName(archive, record: 0, frame: 0, face: 1, setting, outfitIndex: count + 1), clone: false, out Texture2D _); count++) ;
            }

             outfitCountCache.Add(archiveKey, count);
            return count;
        }

        int SelectOutfit(int archive, string setting)
        {
            int outfitCount = GetOutfitCount(archive, setting);

            if (outfitCount == 0)
                return 0;

            return UnityEngine.Random.Range(0, outfitCount) + 1;
        }

        #region Copied from TextureReplacement
        static void SetUv(MeshFilter meshFilter, float x = 0, float y = 0)
        {
            Vector2[] uv = new Vector2[4];
            uv[0] = new Vector2(x, 1 - y);
            uv[1] = new Vector2(1 - x, 1 - y);
            uv[2] = new Vector2(x, y);
            uv[3] = new Vector2(1 - x, y);
            meshFilter.mesh.uv = uv;
        }
        #endregion
    }
}
