import selectors
import socket
import types
import time
HOST = '127.0.0.1'  # The server's hostname or IP address
PORT = 80        # The port used by the server
sel = selectors.DefaultSelector()
i = 0
message = ""
def service_connection(key, mask, message):
    sock = key.fileobj
    data = key.data
    if mask & selectors.EVENT_READ:
        recv_data = sock.recv(1024)  # Should be ready to read
        if recv_data:
            print('received', repr(recv_data),flush=True)
            
    if mask & selectors.EVENT_WRITE:
        if message:
            data.outb += message
            message = ""
            print('sending', repr(data.outb))
            sent = sock.send(data.outb.encode('utf-8'))  # Should be ready to write
            data.outb = data.outb[sent:]

def start_connection(host, port):
    server_addr = (host, port)
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setblocking(False)
    sock.connect_ex(server_addr)
    events = selectors.EVENT_READ | selectors.EVENT_WRITE
    data = types.SimpleNamespace(outb='')
    sel.register(sock, events, data=data)


start_connection(HOST, PORT)
while True:
    events = sel.select(timeout=None)
    for key, mask in events:
        if key.data is None:
            pass
        else:
            service_connection(key, mask, message)
            message = ""
    
    i += 1
    if i % 10 == 0:
        message = f"hello {i}\n"
            
            