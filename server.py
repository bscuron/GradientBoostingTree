import ml
import json
import socket
import struct
import joblib
import pandas as pd
from types import SimpleNamespace

TCP, PAYLOAD_TYPE = None, None
sock, conn = None, None
model, data, lookback_period = None, [], 50

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

            # print(f'[INFO] Client payload received: {payload}')
            handle(conn, payload)

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
    header_bytes = recv_all(struct.calcsize(TCP.HEADER_FORMAT))
    payload_size = struct.unpack(TCP.HEADER_FORMAT, header_bytes)[0]
    payload = recv_all(payload_size)
    return json.loads(payload.decode('utf-8'))

def handle(conn: socket.socket, payload: dict):
    global data, model

    match payload.get('Type'):
        case PAYLOAD_TYPE.TRAIN_ROW:
            if len(data) % 1000 == 0:
                print(f'[INFO] Training payload received')
            data.append(payload)
        case PAYLOAD_TYPE.TRAIN_START:
            print(f'[INFO] Starting training...')
            model = ml.train(data, lookback_period=lookback_period)
            print(f'[INFO] Training finished')
            if payload.get('Save'):
                print(f'[INFO] Saving model...')
                joblib.dump(model, 'model.pkl')
                print(f'[INFO] Model saved')
            send(conn, json.dumps({ 'Type': PAYLOAD_TYPE.TRAIN_FINISH }))
        case PAYLOAD_TYPE.ROW:
            if not model:
                data = []
                print('[INFO] Loading model...')
                model = joblib.load('./model.pkl')
                print('[INFO] Model loaded')
            data.append(payload)
            if len(data) > lookback_period:
                data = data[-(lookback_period+1):]
                # TODO: bad, dont reconstruct dataframe for each prediction
                df = ml.lookback(ml.preprocess(pd.DataFrame(data)), period=lookback_period)
                if not df.empty:
                    pred_class = int(model.predict(df.iloc[[-1]])[0])
                    print(f'[INFO] Prediction: {pred_class}')
                    send(conn, json.dumps({'Type': PAYLOAD_TYPE.CLASS, 'Class': pred_class}))
                else:
                    send(conn, json.dumps({'Type': PAYLOAD_TYPE.CLASS, 'Class': 0 }))
            else:
                send(conn, json.dumps({'Type': PAYLOAD_TYPE.CLASS, 'Class': 0 }))
