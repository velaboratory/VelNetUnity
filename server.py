
import socket
import selectors
import types
sel = selectors.DefaultSelector()
host = '127.0.0.1'
port = 80
lsock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
lsock.bind((host, port))
lsock.listen()
print('listening on', (host, port))
lsock.setblocking(False)
sel.register(lsock, selectors.EVENT_READ, data=None)

clients = []

def accept_wrapper(sock):
    conn, addr = sock.accept()  # Should be ready to read
    print('accepted connection from', addr)
    conn.setblocking(False)
    client = types.SimpleNamespace(addr=addr, inb='', outb='')
    events = selectors.EVENT_READ | selectors.EVENT_WRITE
    sel.register(conn, events, data=client)
    clients.append(client)

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
                #send to other clients
                for client in clients:
                    if client.addr != data.addr:
                        client.outb += message + "\n"
            data.inb += messages[-1]
            
        else:
            print('closing connection to', data.addr)
            sel.unregister(sock)
            sock.close()
    if mask & selectors.EVENT_WRITE:
        if data.outb:
            print('echoing', data.outb, 'to', data.addr)
            sent = sock.send(data.outb.encode('utf-8'))  # Should be ready to write
            data.outb = data.outb[sent:]

while True:
    events = sel.select(timeout=None)
    for key, mask in events:
        if key.data is None:
            accept_wrapper(key.fileobj)
        else:
            service_connection(key, mask)
            
            
            