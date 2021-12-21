
import socket
from _thread import *
import threading
import types

rooms = {} #will be a list of room objects.  #this must be locked when when adding or removing rooms
rooms_lock = threading.Lock()
client_dict = {} #will be a list of client objects.  #this must be locked when adding or removing clients
client_lock = threading.Lock()

HOST = ""
PORT = 80


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

def send_client_message(client, message):
    client.message_lock.acquire()
    client.message_queue.append(message)
    client.message_lock.release()
    client.message_ready.set()

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
            
            roomName = decodedMessage[1]
            if client.room == roomName: #don't join the same room
                pass
            elif (roomName == '-1') and client.room != '': #can't leave a room if you aren't in one
                #leave the room
                rooms_lock.acquire()
                try: 
                    
                    rooms[client.room].clients.remove(client)
                    if(len(rooms[client.room].clients) == 0):
                        del rooms[client.room]
                except Exception as e: 
                    print("not in room")
                rooms_lock.release()
                send_room_message(client.room, f"2:{client.id}:\n")
                send_client_message(client,f"2:{client.id}:\n")
                client.room = ''
            else: #join or create the room
                rooms_lock.acquire()
                if roomName in rooms:
                    #join the room
                    rooms[roomName].clients.append(client)
                else:
                    #create the room and join
                    rooms[roomName] = types.SimpleNamespace(name=roomName,clients=[client],room_lock=threading.Lock())
                rooms_lock.release()

                if (client.room != '') and (client.room != roomName): #client left the previous room
                    send_room_message(client.room, f"2:{client.id}:{roomName}:\n") 
                
                client.room = roomName #client joins the new room
                #send a message to the clients new room that they joined!
                send_room_message(roomName, f"2:{client.id}:{client.room}\n")
            

                
            
        if messageType == '3' and len(decodedMessage) > 2:
            subMessageType = decodedMessage[1]
            if subMessageType == '0':
                #send a message to everyone in the room (not synced)
                send_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n",client)
            elif subMessageType == '1':
                send_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n")
            elif subMessageType == '2':
                #send a message to everyone in the room (not synced)
                send_synced_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n",client)
            elif subMessageType == '3':
                send_synced_room_message(client.room,f"3:{client.id}:{decodedMessage[2]}\n")

def client_read_thread(conn, addr, client):
    global rooms
    global rooms_lock
    global client_dict
    global client_lock
    buffer = bytearray()
    state = 0
    buffer_size = 0
    #valid messages are at least 2 bytes (size)

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
            buffer.extend(recv_data) #read
            if (state == 0) and (len(buffer) > 2):
                buffer_size = int.from_bytes(buffer[0:2], byteorder='big')
                state = 1
            if (state == 1) and (len(buffer) >= buffer_size):
                #we have a complete packet, process it
                message = buffer[2:buffer_size]
                decode_message(client,message)
                buffer = buffer[buffer_size:]
                state = 0

    while not client.write_thread_dead:
        client.message_ready.set()
        pass
    #now we can kill the client, removing the client from the rooms
    client_lock.acquire()
    rooms_lock.acquire()
    if client.room != '':
        rooms[client.room].clients.remove(client)
        if(len(rooms[client.room].clients) == 0):
            del rooms[client.room]
    del client_dict[client.id] #remove the client from the list of clients...
    rooms_lock.release()
    client_lock.release()
    send_room_message(client.room, f"2:{client.id}:\n")
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
        client = types.SimpleNamespace(id=next_client_id, 
                                        alive=True, 
                                        message_queue=[],
                                        message_lock=threading.Lock(), 
                                        inb='', #read buffer
                                        message_ready=threading.Event(),
                                        logged_in=False,
                                        username='',
                                        room='',
                                        write_thread_dead=False
                                        )
        client_lock.acquire()
        client_dict[next_client_id] = client
        client_lock.release()

        next_client_id += 1
        
        start_new_thread(client_read_thread, (c, addr, client))
        start_new_thread(client_write_thread, (c, addr, client))


