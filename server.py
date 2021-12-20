
import socket
import selectors
import types

def decodeMessage(message, addr):
    global last_client_id
    global rooms
    global clients
    global temporary_clients
    # first get the client sending the message
    client = temporary_clients[addr]
    decodedMessage = message.split(":")
    if len(decodedMessage) < 1: print("Invalid message received"); return
    messageType = decodedMessage[0]

    if not client.logged_in and messageType == '0' and len(decodedMessage) == 3:
        client.username = decodedMessage[1]
        client.id = last_client_id
        last_client_id = last_client_id+1
        #probaby check password too against a database of some sort where we store lots of good stuff
        client.logged_in = True
        #add to this list too
        clients[client.id] = client
        print("sending back login success")
        client.outb += f"0:{client.id}:\n"
    elif client.logged_in:
        if messageType == '1':
            
            response = "1:" + ",".join([room.name+"-"+str(len(room.clients)) for room in rooms.values()]) + "\n"
            client.outb += response
        if messageType == '2' and len(decodedMessage) > 1:
            #join or create a room
            roomName = decodedMessage[1]
            if roomName == '-1':
                #leave the room
                try: 
                    rooms[client.room].clients.remove(client)
                    if(len(rooms[client.room].clients) == 0):
                        del rooms[client.room]
                except Exception as e: 
                    print("not in room")
                client.room = ''
            else:
                if roomName in rooms:
                    #join the room
                    rooms[roomName].clients.append(client)
                else:
                    #create the room and join
                    rooms[roomName] = types.SimpleNamespace(name=roomName,clients=[client])
                client.room = roomName #client joins the room
                #send everyone in the room a message
                for client in rooms[roomName].clients:
                    client.outb += f"2:{client.id}:1\n"
        if messageType == '3' and len(decodedMessage) > 2:
            subMessageType = decodedMessage[1]
            if subMessageType == '0':
                #send a message to everyone in the room
                for c in rooms[client.room].clients:
                    c.outb += f"3:{client.id}:{decodedMessage[2]}\n"
                
            elif subMessageType == '1':
                for c in rooms[client.room].clients:
                    if client.id != c.id:
                        c.outb += f"3:{client.id}:{decodedMessage[2]}\n"
                pass
            elif subMessageType == '2':
                #send a message to the client ids indicated
                
                pass


def accept_wrapper(sock):
    conn, addr = sock.accept()  # Should be ready to read
    print('accepted connection from', addr)
    conn.setblocking(False)
    client = types.SimpleNamespace(id=-1, addr=addr, inb='', outb='', logged_in=False, username='',room='') #surrogate for class
    events = selectors.EVENT_READ | selectors.EVENT_WRITE
    sel.register(conn, events, data=client)
    temporary_clients[addr] = client #add to the clients dictionary


def service_connection(key, mask):
    sock = key.fileobj
    data = key.data

    if mask & selectors.EVENT_READ:
        recv_data = sock.recv(1024)  # Should be ready to read
        if recv_data:
            m = recv_data.decode("utf-8")
            messages = m.split("\n")
            if len(messages) > 1:
                messages[0]= data.inb + messages[0]
                data.inb = ""
            for message in messages[:-1]:
                decodeMessage(message, data.addr)
            data.inb += messages[-1]
            
        else:
            print('closing connection to', data.addr)
            client = temporary_clients[data.addr]
            temporary_clients.pop(data.addr)
            if client.logged_in:
                clients.pop(client.id)
            if(client.room != ''):
                rooms[client.room].clients.remove(client)
                if(len(rooms[client.room].clients) == 0):
                    del rooms[client.room]
            sel.unregister(sock)
            sock.close()
    if mask & selectors.EVENT_WRITE:
        if data.outb:
            print('echoing', data.outb, 'to', data.addr)
            sent = sock.send(data.outb.encode('utf-8'))  # Should be ready to write
            data.outb = data.outb[sent:]

sel = selectors.DefaultSelector()
host = '127.0.0.1'
port = 80
temporary_clients = {} #organized by addr
clients = {} #clients is a dictionary organized by an increasing id number.  For now, passwords are irrelevant
rooms = {}

with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as lsock:
    lsock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
   
    lsock.bind((host, port))
    lsock.listen()
    print('listening on', (host, port))
    lsock.setblocking(False)
    sel.register(lsock, selectors.EVENT_READ, data=None)


    
    last_client_id = 0

    while True:
        events = sel.select(timeout=None)
        for key, mask in events:
            if key.data is None:
                accept_wrapper(key.fileobj)
            else:
                service_connection(key, mask)
    
            
            
            