
import socket
from _thread import *
import threading
import types

rooms = {} #will be a list of room objects.  #this must be locked when when adding or removing rooms
rooms_lock = threading.Lock()
client_dict = {} #will be a list of client objects.  #this must be locked when adding or removing clients
client_lock = threading.Lock()

HOST = ""
PORT = 3290


def send_synced_room_message(roomName, message, exclude_client=None): #guaranteed to be received by all clients in order, mostly use for handling ownership
    rooms_lock.acquire()
    if roomName in rooms:
        room_lock = rooms[roomName].room_lock
        clients = rooms[roomName].clients
    else:
        rooms_lock.release()
        return
    rooms_lock.release()
    room_lock.acquire()
    for c in clients:
        if (exclude_client != None) and (c.id == exclude_client.id): continue
        send_client_message(c,message)
    room_lock.release()

def send_room_message(roomName, message, exclude_client=None): #not guaranteed to be received by all clients in order
    rooms_lock.acquire()
    if roomName in rooms:
        clients = rooms[roomName].clients
    else:
        rooms_lock.release()
        return
    rooms_lock.release()
    for c in clients:
        if (exclude_client != None) and (c.id == exclude_client.id): continue
        send_client_message(c,message)

def send_group_message(group, message):
    for client_id in group:
        if client_id in client_dict:
            client = client_dict[client_id] #not sure if we need to lock this...I've heard that basic dictionary access is thread safe
            send_client_message(client, message)


def send_client_message(client, message):
    client.message_lock.acquire()
    client.message_queue.append(message)
    client.message_lock.release()
    client.message_ready.set()

def leave_room(client, clientDisconnected=False):
    global rooms
    global rooms_lock
    choseNewMaster = False
    newMasterId = -1
    rooms_lock.acquire()
    try: 
        rooms[client.room].clients.remove(client)
        if(len(rooms[client.room].clients) == 0):
            del rooms[client.room]
        elif rooms[client.room].master == client:
            rooms[client.room].master = rooms[client.room].clients[0]
            newMasterId = rooms[client.room].master.id
            choseNewMaster = True
            

    except Exception as e: 
        print("not in room")
    rooms_lock.release()
    send_room_message(client.room, f"2:{client.id}:\n") #client not in the room anymore
    if not clientDisconnected:
        send_client_message(client,f"2:{client.id}:\n") #so send again to them
    else:
        client_lock.acquire()
        del client_dict[client.id] #remove the client from the list of clients...
        client_lock.release()
    if choseNewMaster:
        send_room_message(client.room,f"4:{newMasterId}\n")
    client.room = ""
    
def decode_message(client,message):
    global rooms
    global rooms_lock
    decodedMessage = message.split(":")
    if len(decodedMessage) < 1: print("Invalid message received"); return
    messageType = decodedMessage[0]

    if not client.logged_in and messageType == '0' and len(decodedMessage) == 3:
        client.username = decodedMessage[1]
        #probaby check password too against a database of some sort where we store lots of good stuff
        client.logged_in = True
        send_client_message(client,f"0:{client.id}:\n")
        
    elif client.logged_in:
        if messageType == '1':
            rooms_lock.acquire()
            response = "1:" + ",".join([room.name+"-"+str(len(room.clients)) for room in rooms.values()]) + "\n"
            rooms_lock.release()
            send_client_message(client,response)
        if messageType == '2' and len(decodedMessage) > 1:

            #join or create a room
            print("request to join " + decodedMessage[1] + " from " + str(client.id))
            roomName = decodedMessage[1]
            if client.room == roomName: #don't join the same room
                print("Client trying to join the same room")
                pass
            elif (roomName == '-1') and client.room != '': #can't leave a room if you aren't in one
                #leave the room
                leave_room(client)
            elif roomName != '': #join or create the room
                
                
                if client.room != '':
                    #leave that room
                    leave_room(client)
                
                masterId = -1
                rooms_lock.acquire()

                if roomName in rooms:
                    #join the room
                    rooms[roomName].clients.append(client)
                    masterId = rooms[roomName].master.id
                else:
                    #create the room and join it as master
                    rooms[roomName] = types.SimpleNamespace(name=roomName,clients=[client],master=client, room_lock=threading.Lock())
                    masterId = client.id

                current_clients = rooms[roomName].clients
                rooms_lock.release()
                
                client.room = roomName #client joins the new room
                #send a message to the clients new room that they joined!
                send_room_message(roomName, f"2:{client.id}:{client.room}\n")
                
                
                for c in current_clients: #tell that client about all the other clients in the room
                    if c.id != client.id:
                        send_client_message(client,f"2:{c.id}:{client.room}\n")
                
                send_client_message(client, f"4:{masterId}\n") #tell the client who the master is
            

                
            
        if messageType == '3' and len(decodedMessage) > 2:
            subMessageType = decodedMessage[1]
            if subMessageType == '0':
                #send a message to everyone else in the room (not synced)
                send_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n",client)
            elif subMessageType == '1': #everyone including the client who sent it 
                send_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n")
            elif subMessageType == '2': #everyone but the client, ensures order within the room
                send_synced_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n",client)
            elif subMessageType == '3': #everyone including the client, ensuring order
                send_synced_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n")
        if messageType == '4' and len(decodedMessage) > 2:
            if decodedMessage[1] in client.groups:
                send_group_message(client.groups[decodedMessage[1]],f"3:{client.id}:{decodedMessage[2]}\n")
        if messageType == '5' and len(decodedMessage) > 2:
            #specify a new group of clients
            group_clients = []
            for c in decodedMessage[2:]:
                try: group_clients.append(int(c))
                except: pass
            client.groups[decodedMessage[1]] = group_clients
            print("got new group: " + str(client.groups))
        
def client_read_thread(conn, addr, client):
    global rooms
    global rooms_lock
    global client_dict
    global client_lock
    while client.alive:
        try:
            recv_data = conn.recv(1024)
        except Exception as e:
            client.alive = False
            client.message_ready.set()
            continue
        if not recv_data:
            client.alive = False
            client.message_ready.set() #in case it's waiting for a message
        else:
            m = recv_data.decode("utf-8")
            messages = m.split("\n")
            if len(messages) > 1:
                messages[0]= client.inb + messages[0]
                client.inb = ""
            for message in messages[:-1]:
                decode_message(client, message)
            client.inb += messages[-1]
    while not client.write_thread_dead:
        client.message_ready.set()
        pass
    #now we can kill the client, removing the client from the rooms
    

    leave_room(client,True)

    print("client destroyed")
def client_write_thread(conn, addr, client):
    while client.alive:
        
        client.message_lock.acquire()
        for message in client.message_queue:
            try:
                conn.sendall(message.encode('utf-8'))
            except:
                break #if the client is dead the read thread will catch it
        client.message_queue = []
        client.message_lock.release()
        client.message_ready.wait()
        client.message_ready.clear()
    client.write_thread_dead = True

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
   
    sock.bind((HOST, PORT))
    sock.listen()
    next_client_id = 0
    while True:

        c, addr = sock.accept() #blocks until a connection is made
        c.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
        client = types.SimpleNamespace(id=next_client_id, 
                                        alive=True, 
                                        message_queue=[],
                                        message_lock=threading.Lock(), 
                                        inb='', #read buffer
                                        message_ready=threading.Event(),
                                        logged_in=False,
                                        username='',
                                        room='',
                                        groups={}, # a dictionary of groups that you may send to.  groups are lists of user ids
                                        write_thread_dead=False
                                        )
        client_lock.acquire()
        client_dict[next_client_id] = client
        client_lock.release()

        next_client_id += 1
        
        start_new_thread(client_read_thread, (c, addr, client))
        start_new_thread(client_write_thread, (c, addr, client))


