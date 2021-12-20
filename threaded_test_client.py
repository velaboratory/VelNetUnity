import socket
import time
from _thread import *
import threading

server_addr = ('127.0.0.1', 80)
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect_ex(server_addr)




def readThread(sock):

    while True:
        data = sock.recv(1024)
        print(data.decode('utf-8'));
    
def writeThread(sock):
    i = 0
    sock.sendall('0::\n'.encode('utf-8'))
    sock.sendall('2:myroom\n'.encode('utf-8'))

    
    while True:
            sock.sendall(f'3:0:{"thasdl;fjasd;lfjasl;dfjal;skdjlask;dflasd;jkjfjkjfsfjfjakfjafjdfjakjflfjadjf;jfakdjfdjfakdjfsdj;ldjf;laskdflsdjfasdkjfkdjflskdjfskdjflkfjlkdjfskdjfkjfskdjf;kfjs;kfjadkfjas;ldfalsdkfsdkfjasdkjfasdkfjlkdjfkdjflkdjf;djfadkfjaldkfjalkfja;kfja;kfjadkfjadkfja;sdkfa;dkfj;dfkjaslkfjas;dkfs;dkfjsldfjasdfjaldfjaldkfj;lkj"}\n'.encode('utf-8'))
            i = i+1
            time.sleep(0.1)
    
    

start_new_thread(readThread,(sock,))
start_new_thread(writeThread,(sock,))

while True:
    time.sleep(1)

sock.close()

    

