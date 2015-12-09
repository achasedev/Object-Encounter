﻿using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using System.Collections;
//using UnityStandardAssets.CrossPlatformInput;

public class PropController : NetworkBehaviour {

    public GameObject DefaultModel;
    public Text UIText;
    public int MaxHealth = 100;
    public float InteractDistance = 10f;

    private GameObject playerModel;
    private GameObject graphics;
    private GameObject cam;
    private Camera myCamera;

    private Rigidbody rigidBody;

    private DoorController doorController;

    private Vector3 spawnPosition;
    private Quaternion spawnRotation;

    [SyncVar]
    private bool isActive;

    [SyncVar, HideInInspector]
    public int health;
    [SyncVar]
    private bool dead;

    public class PropMessage : MessageBase {
        public enum Type { Change, Death, Respawn };
        public static short TypeId = 555;
        public Type msgType;
        public NetworkInstanceId player;
        public NetworkInstanceId prop;
    }


    // Use this for initialization
    void Start() {
        // hide and lock the cursor
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        // get necessary references
        graphics = transform.Find("Graphics").gameObject;
        playerModel = graphics.transform.Find("Player Model").gameObject;
        cam = transform.Find("Camera").gameObject;
        myCamera = cam.GetComponent<Camera>();
        rigidBody = GetComponent<Rigidbody>();
        doorController = GetComponent<DoorController>();

        // life status
        health = MaxHealth;
        dead = false;

        // record the spawn place, used to respawn
        spawnPosition = transform.position;
        spawnRotation = transform.rotation;

        // disable UI for other players
        if (!isLocalPlayer) {
            cam.transform.Find("Canvas").gameObject.SetActive(false);
        }

        // setup handlers
        if (isServer) {
            NetworkServer.RegisterHandler(PropMessage.TypeId, OnPropMessageServer);
        }
        if (isLocalPlayer) {
            NetworkClient.allClients[0].RegisterHandler(PropMessage.TypeId, OnPropMessageClient);
        }

        // TEMPORARY SOLUTION: the server is the hunter
        if (isServer) {
            if (isLocalPlayer) {
                isActive = false;
            }
            else {
                isActive = true;
            }
        }
    }

    void OnApplicationFocus() {
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
    }

    void OnDestroy() {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    // Update is called once per frame
    void Update() {
        // only do the following things on local player
        if (!isLocalPlayer)
            return;

        // Return if I am not a prop player
        if (!isActive)
            return;

        // if dead, no more action
        if (dead) {
            return;
        }
        else if (health <= 0) {
            DieLocal();
            SendPropMessageDie();
            return;
        }

        // the aim is locked at the center of the screen
        Ray camRay = myCamera.ScreenPointToRay(new Vector3(Screen.width / 2, Screen.height / 2));
        RaycastHit objectHit;
        if (Physics.Raycast(camRay, out objectHit, InteractDistance)) {
            GameObject obj = objectHit.transform.gameObject;
            // aiming at a prop
            if (obj.tag.Equals("Prop")) {
                UIText.text = "Press \"Fire1\" to change into the " + obj.name;
                // if Fire1 down, change into that object
                if (Input.GetButtonDown("Fire1")) {
                    SendPropMessageChange(obj);
                }
            }
            else if (obj.tag.Equals("Door")) { // aiming at a door
                UIText.text = "Press \"Fire2\" to open/close the door";
                // if Fire1 down, open/close that door
                if (Input.GetButtonDown("Fire2")) {
                    doorController.CmdMoveDoor(obj);
                }
            }
            else {
                UIText.text = "Test";
            }
        }
        else {
            UIText.text = "Test";
        }
    }

    // send message when the player change to a prop
    private void SendPropMessageChange(GameObject objHit) {
        NetworkIdentity objIdtt = objHit.GetComponent<NetworkIdentity>() as NetworkIdentity;
        NetworkIdentity playerIdtt = gameObject.GetComponent<NetworkIdentity>() as NetworkIdentity;
        if (objIdtt == null || playerIdtt == null)
            return;

        PropMessage msg = new PropMessage();
        msg.msgType = PropMessage.Type.Change;
        msg.prop = objIdtt.netId;
        msg.player = playerIdtt.netId;
        NetworkClient.allClients[0].Send(PropMessage.TypeId, msg);
        Debug.Log("Client sent: " + msg.player + " " + msg.prop);
    }

    // send message when the player dies
    private void SendPropMessageDie() {
        NetworkIdentity playerIdtt = gameObject.GetComponent<NetworkIdentity>() as NetworkIdentity;
        if (playerIdtt == null)
            return;

        PropMessage msg = new PropMessage();
        msg.msgType = PropMessage.Type.Death;
        msg.player = playerIdtt.netId;
        NetworkClient.allClients[0].Send(PropMessage.TypeId, msg);
        Debug.Log("Client sent: " + msg.player);
    }

    // send message when the player is respawned
    private void SendPropMessageRespawn() {
        NetworkIdentity playerIdtt = gameObject.GetComponent<NetworkIdentity>() as NetworkIdentity;
        if (playerIdtt == null)
            return;

        PropMessage msg = new PropMessage();
        msg.msgType = PropMessage.Type.Respawn;
        msg.player = playerIdtt.netId;
        NetworkClient.allClients[0].Send(PropMessage.TypeId, msg);
        Debug.Log("Client sent: " + msg.player);
    }

    // server message handler
    private void OnPropMessageServer(NetworkMessage netMsg) {
        PropMessage msg = netMsg.ReadMessage<PropMessage>();
        NetworkServer.SendToAll(PropMessage.TypeId, msg);
        Debug.Log("Server received: " + msg.player + " " + msg.prop);
    }

    // client message handlers
    private void OnPropMessageClient(NetworkMessage netMsg) {
        PropMessage msg = netMsg.ReadMessage<PropMessage>();
        Debug.Log("Client received: " + msg.player + " " + msg.prop);

        switch (msg.msgType) {
            case PropMessage.Type.Change: {
                    GameObject playerChanged = ClientScene.FindLocalObject(msg.player);
                    PropController pc = playerChanged.GetComponent<PropController>();
                    //GameObject prop = ClientScene.prefabs[msg.prop];
                    GameObject obj = ClientScene.FindLocalObject(msg.prop);
                    pc.UpdateModel(obj);
                    break;
                }
            case PropMessage.Type.Death: {
                    GameObject playerDied = ClientScene.FindLocalObject(msg.player);
                    playerDied.GetComponent<PropController>().DieClient();
                    break;
                }
            case PropMessage.Type.Respawn: {
                    GameObject playerDied = ClientScene.FindLocalObject(msg.player);
                    playerDied.GetComponent<PropController>().RespawnClient();
                    break;
                }
        }
    }

    // update the model who turned into a prop in all clients
    public void UpdateModel(GameObject prop) {
        Mesh targetMesh = prop.GetComponent<MeshFilter>().mesh;
        // Since the mesh is to be changed, make the rigid body sleep and adjust the height
        rigidBody.Sleep();
        rigidBody.MovePosition(rigidBody.position + new Vector3(0,
            -(targetMesh.bounds.min.y - playerModel.GetComponent<MeshFilter>().mesh.bounds.min.y),
            0));

        // change the mesh
        //playerModel.GetComponent<MeshFilter>().mesh = targetMesh;
        //playerModel.GetComponent<MeshCollider>().sharedMesh = null;
        //playerModel.GetComponent<MeshCollider>().sharedMesh = obj.GetComponent<MeshCollider>().sharedMesh;
        Destroy(playerModel);
        playerModel = Instantiate(prop, graphics.transform.position, graphics.transform.rotation) as GameObject;
        playerModel.transform.parent = graphics.transform;
        playerModel.tag = "Player";
        MeshCollider meshCollider = playerModel.GetComponent<MeshCollider>();
        if (meshCollider != null) {
            meshCollider.convex = true; // non-kinematic rigid body can only have a convex mesh collider
        }

        // also adjust the camera to the front face of the new model
        cam.transform.localPosition = new Vector3(0, 0, targetMesh.bounds.max.z);
    }

    // should be called by the hunter who shot me
    public void TakeDamage(int damage) {
        // already dead: do nothing
        if (health <= 0)
            return;

        health -= damage;
        if (health <= 0) {
            health = 0;
        }
    }

    // called on death, only local player
    private void DieLocal() {
        GetComponent<PlayerController>().enabled = false;
        StartCoroutine(WaitRespawn(5));
    }

    // called on death, every player
    private void DieClient() {
        dead = true;
        playerModel.GetComponent<Renderer>().material.color = new Color(1.0f, 0.0f, 0.0f);
    }

    // display respawn message
    private IEnumerator WaitRespawn(int seconds) {
        string template = "You died.\nRespawn in {0} second(s).";
        for (int i = 0; i < seconds; ++i) {
            UIText.text = string.Format(template, seconds - i);
            yield return new WaitForSeconds(1);
        }
        RespawnLocal();
        SendPropMessageRespawn();
        UIText.text = "";
    }

    // called on respawn, local player
    private void RespawnLocal() {
        GetComponent<PlayerController>().enabled = true;
    }

    // called on respawn, every player
    private void RespawnClient() {
        // FIRST PART: 
        // reset the model to the default model
        Mesh targetMesh = DefaultModel.GetComponent<MeshFilter>().sharedMesh;
        // transport the player to the spawn point
        rigidBody.Sleep();
        rigidBody.position = spawnPosition;
        rigidBody.rotation = spawnRotation;

        // change the mesh
        //playerModel.GetComponent<MeshFilter>().mesh = targetMesh;
        //playerModel.GetComponent<MeshCollider>().sharedMesh = null;
        //playerModel.GetComponent<MeshCollider>().sharedMesh = obj.GetComponent<MeshCollider>().sharedMesh;
        Destroy(playerModel);
        playerModel = Instantiate(DefaultModel, graphics.transform.position, graphics.transform.rotation) as GameObject;
        playerModel.transform.parent = graphics.transform;
        playerModel.tag = "Player";
        MeshCollider meshCollider = playerModel.GetComponent<MeshCollider>();
        if (meshCollider != null) {
            meshCollider.convex = true; // non-kinematic rigid body can only have a convex mesh collider
        }

        // also adjust the camera to the front face of the new model
        cam.transform.localPosition = new Vector3(0, 0, targetMesh.bounds.max.z);

        // SECOND PART:
        // reset health the dead status
        // ONLY ON SERVER!
        if (!isServer)
            return;
        health = MaxHealth;
        dead = false;
    }
}
