import handler
import json
import socket
import struct
from types import SimpleNamespace

TCP, PAYLOAD_TYPE = None, None
sock, conn = None, None

def main(CONFIG: SimpleNamespace):
    global TCP, PAYLOAD_TYPE
    TCP = CONFIG.TCP
    PAYLOAD_TYPE = CONFIG.PAYLOAD_TYPE

    sock = socket.socket()
    sock.bind((TCP.HOST, TCP.PORT))
    sock.listen()
    print(f'[INFO] TCP Server started (HOST={TCP.HOST}, PORT={TCP.PORT})')

    while True:
        conn, _ = accept(sock)
        while True:
            try:
                payload = receive(conn)
            except ConnectionResetError:
                conn.close()
                print(f'[INFO] Client connection lost')
                break

            print(f'[INFO] Client payload received: {payload}')
            handler.handle(payload)
            send(conn, json.dumps({ 'type': PAYLOAD_TYPE.ACK }))

def accept(sock: socket.socket):
    print(f'[INFO] Waiting for client connection...')
    conn, addr = sock.accept()
    print(f'[INFO] Client connected')
    return conn, addr

def send(conn: socket.socket, payload: str | dict | bytes):
    def pack(payload_bytes: bytes) -> bytes:
        return struct.pack(TCP.HEADER_FORMAT, len(payload_bytes)) + payload_bytes

    if isinstance(payload, dict):
        payload_bytes = json.dumps(payload).encode('utf-8')
    elif isinstance(payload, str):
        payload_bytes = payload.encode('utf-8')
    elif isinstance(payload, bytes):
        payload_bytes = payload
    else:
        raise TypeError(f'[ERROR] Invalid payload type: {type(payload)}')

    packed_payload = pack(payload_bytes)
    conn.sendall(packed_payload)

def receive(conn: socket.socket) -> dict:
    def recv_all(n: int) -> bytes:
        payload = b''
        while len(payload) < n:
            payload += conn.recv(n - len(payload))
        return payload
    header_bytes = recv_all(struct.calcsize(TCP.HEADER_FORMAT)) # TODO: cache header size
    payload_size = struct.unpack(TCP.HEADER_FORMAT, header_bytes)[0]
    payload = recv_all(payload_size)
    return json.loads(payload.decode('utf-8'))

