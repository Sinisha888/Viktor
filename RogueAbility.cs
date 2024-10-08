using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;

public class RogueAbility : NetworkBehaviour
{
    [Header("Hide Variables")]
    [SyncVar] public bool hidden; // bool for stealth check
    private float delayTimer = 0f;
    private float delayDuration = 5f;
    private bool isCoroutineStarted = false; // check for drain
    public int drainPerTick = 40; // how much is drained
    public float tickInterval = 10f; // time wich the drain is ticking 
    public float duration = 50f; // how much time does it drain
    public float slowPerTick = 0.05f;
    public GameObject rogueModel;
    [SyncVar, HideInInspector] public bool combat;
    Recount recount;

    [System.Serializable]
    public class RecastedSkills
    {
        public string mainSkill;
        public string recastedSkill;
    }

    public List<RecastedSkills> recastSkills = new();
    Dictionary<string, float> slotTimers = new Dictionary<string, float>();

    [Header("Move Speed")]
    public LinearFloat moveSpeedWhileStealthed;
    public LinearFloat normalMoveSpeed;

    [Header("Incapacitate")]
    [SyncVar ,HideInInspector] public bool backStabStun = false;

    [Tooltip("recasting can only be cast within 5 seconds after casting the base ability and requires a “Combat Advantage” buff on the caster.")]
    public float leverageAdvantage = 5f;
    [HideInInspector] public bool combatAdvantage;
    [HideInInspector] public BuffSkill combatAdvantageBuff;

    [Header("Hateful strike")]
    [HideInInspector] public ScriptableSkill critDamageBuff;
    [SyncVar,HideInInspector] public bool critDamage = false;
    public LayerMask enemyLayer;
    
    [SyncVar,HideInInspector] public int focusUsed;
    [SyncVar,HideInInspector] public bool vanish;
    [HideInInspector] public bool flurry;
    [HideInInspector] public float flurryTime = 5f;
    [SyncVar,HideInInspector] public bool finalStrike = false;

    [SyncVar(hook = nameof(OnInitialConsumedChanged))]
    [HideInInspector] public bool initialConsumed = false;
    private string finalX = "";
    [SyncVar,HideInInspector] public bool bleeding;
    [SyncVar,HideInInspector] public bool grapLine = false; 
    [Header("Cast Camera")]
    public Camera castCamera;

    [Header("Circle and Radius")]
    [SyncVar] public float circleRadius = 1f;
    public int vertexCount = 40;

    [Header("Ground Layer for Raycsting")]
    public LayerMask groundLayer;
    
    [Header("Mouse and Circle Effects")]
    public GameObject mouseVfx;
    public GameObject circlePrefab;

    [Header("QuickGrapple")]
    [SyncVar] public bool quickGrapple;
    public float hookLerpSpeed = 5f; // Adjust this value to control the speed of the grappling hook movement
    private Vector3 targetPosition;
    //private float lerpT;
    
    [Header("Maiming Stun")]
    [SyncVar,HideInInspector] public Entity stunTarget;
    [HideInInspector] public int consumeMana;
    private bool lastInCombatStatus = false;

    private void Start() 
    {
        circlePrefab.SetActive(false);
        mouseVfx.SetActive(false);
        recount = FindObjectOfType<Recount>();
        focusUsed = 0;
        consumeMana = 0;

        Player player = GetComponent<Player>();  // Assuming this script is attached to the player object
        if (player != null)
        {
            StartCoroutine(CheckAggroListRoutine(player));
        }
    }

    public override void OnStartLocalPlayer()
    {
        castCamera = Camera.main;
    }
    
    void Update()
    {
        if (isOwned)
        {
            Player player = Player.localPlayer;
            
            if(quickGrapple == true)
            {
                circlePrefab.SetActive(true);
                
                Ray ray = castCamera.ScreenPointToRay(Input.mousePosition);
                if (EventSystem.current.IsPointerOverGameObject())
                {
                    mouseVfx.SetActive(false);
                    // Check if left mouse button is clicked
                    if (Input.GetMouseButtonDown(0))
                    {
                        DisableCast(); // Call your method here
                    }
                    return; // Stops further execution for this frame
                }
                if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, groundLayer))
                {
                    // Get the hit point
                    Vector3 hitPoint = hit.point;

                    // Check if the hit object is indeed part of the ground layer
                    if (((1 << hit.collider.gameObject.layer) & groundLayer) != 0)
                    {
                        float distanceFromCenter = Vector3.Distance(transform.position, hitPoint);

                        if (distanceFromCenter <= circleRadius)
                        {
                            // Update the hook position
                            mouseVfx.SetActive(true);
                            mouseVfx.transform.position = hitPoint;
                            if (Input.GetMouseButtonDown(0))
                            {
                                Grappling(hitPoint,player);
                            }
                        }
                        else
                        {
                            mouseVfx.SetActive(false);// remove this if needs to stay on circle
                        }
                    }
                    else
                    {
                        mouseVfx.SetActive(false);// remove this if needs to stay on circle
                    }
                    
                   
                }
            }

            //Maiming Strike
            if(bleeding == true)
            {
                float distance = Vector3.Distance(player.transform.position, stunTarget.transform.position);
                if(distance > 5f)
                {
                    
                    player.combat.DealDamageAt(stunTarget, 0 , 1, 1);
                    //player.target.skills.ConsumeBufforDebuff("MaimingStrikeDebuff");
                    bleeding = false;
                }
            }

            if (initialConsumed == true)
            {
                finalStrike = true;
            }
            else
            {
                // Reset everything if initialConsumed is not true
                initialConsumed = false;
                finalStrike = false;
                finalX = "";
            
                
            }

            //CombatAdvantage buff
            if(focusUsed >= 100)
            {
                CmdRecastingSkills("ProvokingStrike","AdvantageousStrike" , player);
            }

            if (player.skills.CheckBuffOrDebuff("TestGuardBuff"))
            {   
                flurryTime -= Time.deltaTime;
                for (int i = 0; i < player.skills.skillTemplates.Length; i++)
                {
                    ScriptableSkill skill = player.skills.skillTemplates[i];
                    if(skill.skillName == "Stab")
                    {
                        skill.cooldown = skill.baseCooldown;
                        skill.castTime = skill.baseCastTime;
                    }
                }     
            }
            else
            {
                for (int i = 0; i < player.skills.skillTemplates.Length; i++)
                {
                    ScriptableSkill skill = player.skills.skillTemplates[i];
                    if(skill.skillName == "Stab")
                    {
                        skill.baseCooldown = skill.cooldown;
                        skill.baseCastTime = skill.castTime;
                    }
                }
            }
            
            if(grapLine == true)
            {
                CmdGrappling(player,player.target);
            }
           

            UpdatePlayerSkills(player);

            
            

            if(vanish == true)
            {
                MonsterAggroList(player);
            }

            if(Input.GetKey(KeyCode.Escape))
            {
                DisableCast();
            }

            
            //combat = recount.inCombat;
            HasTurnedBack(player ,player.target);
            Stealth(player);
        }
    }

    private void OnInitialConsumedChanged(bool oldValue, bool newValue)
    {
        if (newValue == true)
        {
            StartCoroutine(ResetInitialConsumedAfterDelay());
        }
    }

    private IEnumerator ResetInitialConsumedAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        initialConsumed = false;
        Debug.Log(initialConsumed);
    }

    [Command]
    private void Incapacitate(Entity player, Entity target)
    {
        for (int i = 0; i < player.skills.skills.Count; ++i)
        {
            Skill entry = player.skills.skills[i];
            if (entry.IsCasting())
            {
                Debug.Log(entry.IsCasting());
                player.combat.DealDamageAt(target,0,100,4);
                hidden = false;
                Debug.Log(hidden);

                Collider[] colList = player.GetComponentsInChildren<Collider>();
                for (int j = 0; j < colList.Length; j++)
                {
                    colList[j].enabled = true;
                }
                rogueModel.SetActive(true);

                // Call the ClientRpc to update clients
                RcpStealth(true);
                player.skills.ConsumeBufforDebuff("Hide");
                isCoroutineStarted = false;
            }
        }
        
    }

    [Command]
    private void Stealth(Player player)
    {
        if (hidden == true)
        {
            // Disable all colliders and hide the rogue model
            Collider[] colList = player.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colList.Length; i++)
            {
                colList[i].enabled = false;
            }
            rogueModel.SetActive(false);
            player.Speed = moveSpeedWhileStealthed;
            // Call the ClientRpc to update clients
            RcpStealth(false);

            if (!isCoroutineStarted)
            {
                delayTimer += Time.deltaTime;

                if (delayTimer >= delayDuration)
                {
                    StartCoroutine(DrainFocus(player));
                    isCoroutineStarted = true;
                } 
            }

           
        }
        else
        {
            // Reactivate all colliders and show the rogue model
            Collider[] colList = player.GetComponentsInChildren<Collider>();
            for (int i = 0; i < colList.Length; i++)
            {
                colList[i].enabled = true;
            }
            rogueModel.SetActive(true);
            player.Speed = normalMoveSpeed;
            // Call the ClientRpc to update clients
            RcpStealth(true);
            player.skills.ConsumeBufforDebuff("Hide");
            isCoroutineStarted = false;
            //StopCoroutine(DrainFocus(player));  
        }
    }

    [ClientRpc]
    private void RcpStealth(bool isVisible)
    {
        rogueModel.SetActive(isVisible);
    }
    
    
    [Command]
    public void HasTurnedBack(Entity player, Entity target)
    {
        if (target == null || player == null)
        {
            //Debug.Log("Early exit: No target or player available");
            return;
        }

        Vector3 directionToPlayer = player.transform.position - target.transform.position;
        float dotProduct = Vector3.Dot(target.transform.forward, directionToPlayer.normalized);
        bool hasTurnedBack = dotProduct < -0.6f;
        //Debug.Log($"HasTurnedBack check: {hasTurnedBack} (dotProduct: {dotProduct})");

        if (hasTurnedBack)
        {
            //Debug.Log("Target has turned back. Performing backstab and other actions.");
            PerformBackstab(player, target);
        }
       
    }

    
    private void PerformBackstab(Entity player, Entity target)
    {
        if(backStabStun == true)
        {
            foreach (Skill entry in player.skills.skills)
            {
                if (entry.IsCasting())
                {
                    Debug.Log($"Casting {entry.name}: {entry.IsCasting()}");
                    player.combat.DealDamageAt(target, 0, 100, 5);
                    backStabStun = false;
                    hidden = false; // Consider the implications of this global state change
                    Debug.Log($"Hidden state updated: {hidden}");

                    UpdateCollidersAndVisibility(player, true);
                    player.skills.ConsumeBufforDebuff("Hide");
                    isCoroutineStarted = false; // Ensure this is the correct logic flow
                }
            }
        }
        
    }

    
    private void UpdateCollidersAndVisibility(Entity player, bool isEnabled)
    {
        Collider[] colList = player.GetComponentsInChildren<Collider>();
        foreach (Collider col in colList)
        {
            col.enabled = isEnabled;
        }
        rogueModel.SetActive(isEnabled);
        RcpStealth(isEnabled);
    }
        
    [Command]
    private void DisableCast()
    {
        quickGrapple = false;
       
        DisableAoeChecks();
    }

    //[Command]
    public void CombatAdvantageBuff(Entity player)
    {
        if(player.skills.CheckBuffOrDebuff("CombatAdvantage"))
        {
            player.skills.RefreshSkillCooldowns(10);
        }
        else
        {
            player.skills.AddOrRefreshBuff(new Buff(combatAdvantageBuff, 1));
            //combatAdvantageBuff.Apply(player.target, skillLevel:1);
        }
    }

    
    IEnumerator DrainFocus(Entity player)
    {
        float elapsedDuration = 0f;

        while (hidden == true && elapsedDuration < duration)
        {
            yield return new WaitForSeconds(tickInterval);
            ApplyDrainFocus(drainPerTick,player);
            ApplySlow(player);
            elapsedDuration += tickInterval;
           
        }
        delayTimer = 0f;
    }

    [ClientRpc]
    private void ApplyDrainFocus(int drain, Entity player)
    {
        player.mana.current -= drain;
        if(player.mana.current <= 1)
        {
            hidden = false;
            player.skills.ConsumeBufforDebuff("Hide");    
            player.Speed = normalMoveSpeed;
            moveSpeedWhileStealthed.baseValue = normalMoveSpeed.baseValue;
            
        }
    }

    [ClientRpc]
    private void ApplySlow(Entity player)
    {
        float stealthMoveSpeed = moveSpeedWhileStealthed.baseValue -  slowPerTick;
        //Debug.Log(stealthMoveSpeed);
        moveSpeedWhileStealthed.baseValue = stealthMoveSpeed;
       
    }

    [Command]
    public void MonsterAggroList(Entity player)
    {
        Collider[] colliders = Physics.OverlapSphere(player.transform.position, 100, enemyLayer);

        foreach (Collider collider in colliders)
        {
            if (collider.CompareTag("Monster"))
            {
                Monster monster = collider.GetComponentInParent<Monster>();
                if (monster != null)
                {
                    monster.target = null; 
                    monster.RemovePlayerFromAggroList(player.name);
                    vanish = false; 
                }
            }
        }
        
    }

    public IEnumerator CheckAggroListRoutine(Player player)
    {
        while (true)
        {
            if (player != null)
            {
                Collider[] colliders = Physics.OverlapSphere(player.transform.position, 100, enemyLayer);
                bool isPlayerInCombat = false;

                foreach (Collider collider in colliders)
                {
                    if (collider.CompareTag("Monster"))
                    {
                        Monster monster = collider.GetComponentInParent<Monster>();
                        if (monster != null && monster.IsPlayerInAggroList(player.name))
                        {
                            isPlayerInCombat = true;
                            break; // Stop checking once any monster is found to be in aggro
                        }
                    }
                }

                // Update combat status via ClientRpc only if there's a change
                if (isPlayerInCombat != lastInCombatStatus)
                {
                    AgroCheck(player, isPlayerInCombat);
                    lastInCombatStatus = isPlayerInCombat; // Update the last known status
                }
            }
            yield return new WaitForSeconds(0.5f); // Check every half second instead of every frame
        }
    }

    [ClientRpc]
    private void AgroCheck(Player player, bool inCombat)
    {
        if (player.recount != null)
        {
            player.recount.inCombat = inCombat;
        }
    }

    [Command]
    private void CmdRecastingSkills(string skill , string recastSkill,Entity player)
    {
        RecastingSkills(skill,recastSkill , player);
        focusUsed = 0;
    }

    [ClientRpc]
    public void RecastingSkills(string skill , string recastSkill,Entity player)
    {
        for (int i = 0; i < player.GetComponent<PlayerSkillbar>().slots.Length; i++)
        {
            if(player.GetComponent<PlayerSkillbar>().slots[i].reference == skill)
            {
                player.GetComponent<PlayerSkillbar>().slots[i].reference = recastSkill;
            }
        } 
    }
    
    public void UpdatePlayerSkills(Entity player) 
    {
        List<string> keysToRemove = new List<string>();

        foreach (var kvp in slotTimers.ToList())
        {
            string skillKey = kvp.Key;
            slotTimers[skillKey] -= Time.deltaTime;
            //Debug.Log(slotTimers[skillKey]);
            if (slotTimers[skillKey] <= 0)
            {
                for (int i = 0; i < player.GetComponent<PlayerSkillbar>().slots.Length; i++)
                {
                    if (player.GetComponent<PlayerSkillbar>().slots[i].reference == skillKey)
                    {
                        foreach (var recastSkill in recastSkills)
                        {
                            if (recastSkill.recastedSkill == skillKey)
                            {
                                player.GetComponent<PlayerSkillbar>().slots[i].reference = recastSkill.mainSkill;
                                break;
                            }
                        }
                    }
                }
                keysToRemove.Add(skillKey);
            }
        }
        
        foreach (string key in keysToRemove)
        {
            slotTimers.Remove(key);
        }

        foreach (var recastSkill in recastSkills)
        {
            for (int i = 0; i < player.GetComponent<PlayerSkillbar>().slots.Length; i++)
            {
                if (player.GetComponent<PlayerSkillbar>().slots[i].reference != null &&
                    recastSkill.recastedSkill == player.GetComponent<PlayerSkillbar>().slots[i].reference)
                {
                    string skillKey = player.GetComponent<PlayerSkillbar>().slots[i].reference;

                    if (!slotTimers.ContainsKey(skillKey))
                    {
                        slotTimers[skillKey] = leverageAdvantage;

                        if(skillKey == "FinalStrike")
                        {
                            finalX = skillKey;
                        }
                    }
                }
            }
        }
    }

    public void ResetSkillInstantly()
    {
        Player player = Player.localPlayer;
        // Loop through the slots and reset instantly
        for (int i = 0; i < player.GetComponent<PlayerSkillbar>().slots.Length; i++)
        {
            string currentSkill = player.GetComponent<PlayerSkillbar>().slots[i].reference;

            foreach (var recastSkill in recastSkills)
            {
                if (recastSkill.recastedSkill == currentSkill)
                {
                    player.GetComponent<PlayerSkillbar>().slots[i].reference = recastSkill.mainSkill;
                    break;
                }
            }
        }
    }

    public void ChangeCDOnRecastedSkill(string skill, float cooldown)
    {
        Player player = Player.localPlayer;
        for (int i = 0; i < player.skills.skillTemplates.Length; i++)
        {
            if(player.skills.skillTemplates[i].skillName == skill)
            {
                player.skills.skillTemplates[i].cooldown.baseValue = cooldown;
            }
        }
    }

    [ClientRpc]
    public void DrawCircle() // have to add radius
    {
        //circleInstance = Instantiate(circlePrefab, playerTransform.position, Quaternion.identity, playerTransform);
        circlePrefab.transform.localPosition = Vector3.zero;

        LineRenderer lineRenderer = circlePrefab.GetComponent<LineRenderer>();
        //NetworkServer.Spawn(circleInstance,connectionToClient);
        // Ensure the LineRenderer has the correct number of positions
        lineRenderer.positionCount = vertexCount + 1;

        float deltaTheta = (2f * Mathf.PI) / vertexCount;
        float theta = 0f;

        for (int i = 0; i <= vertexCount; i++)
        {
            float x = circleRadius * Mathf.Cos(theta);
            float z = circleRadius * Mathf.Sin(theta);
            Vector3 pos = new Vector3(x, 0f, z);
            lineRenderer.SetPosition(i, pos);
            theta += deltaTheta;
        }
    }

    [ClientRpc]
    private void DisableAoeChecks()
    {
        if(!isOwned) return;
        circlePrefab.SetActive(false);
        mouseVfx.SetActive(false);
    }

    [Command]
    private void Grappling(Vector3 leapPoint, Player player)
    {
        // Calculate the target position and update server logic
        RpcHandleGrappling(leapPoint, player);
        //lerpT = 0f;
        transform.position = leapPoint;  // This will be updated locally and then smoothed out over the network
        quickGrapple = false;
        player.mana.current -= consumeMana;
        consumeMana = 0;
        DisableAoeChecks();
    }

    [ClientRpc]
    private void RpcHandleGrappling(Vector3 leapPoint, Player player)
    {
        // This method updates all clients about the grappling action
        if (!isServer)  // Check if it's not the server, since server has already set these
        {
            //lerpT = 0f;
            transform.position = leapPoint;
            quickGrapple = false;
        }
        
        // Visual or other client-specific effects related to grappling
        //StartGrapplingAnimation(leapPoint, player);
    }


    

    [Command]
    private void CmdGrappling(Entity player, Entity target)
    {
        // Calculate the target position and update server logic
        RpcHandleGrappling(player, target);
        //lerpT = 0f;
        player.transform.position = target.transform.position;  // This will be updated locally and then smoothed out over the network
        player.mana.current -= consumeMana;
        consumeMana = 0;
        grapLine = false;
        // DisableAoeChecks(); // Uncomment if AoE checks need to be disabled
    }

    [ClientRpc]
    private void RpcHandleGrappling(Entity player, Entity target)
    {
        // This method updates all clients about the grappling action
        if (!isServer)  // Check if it's not the server, since server has already set these
        {
            //lerpT = 0f;
            player.transform.position = target.transform.position;
        }
        
        // Visual or other client-specific effects related to grappling
        //StartGrapplingAnimation(player, target);
    }
}