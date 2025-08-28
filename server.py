import ml
import json
import socket
import struct
import joblib
import pandas as pd
from types import SimpleNamespace
from collections import deque

tcp, payload_type = None, None
sock, conn = None, None
model, lookback_period = None, 50
train_rows, pred_rows = [], []

def main(config: SimpleNamespace):
    global tcp, payload_type
    tcp = config.tcp
    payload_type = config.payload_type

    sock = socket.socket()
    sock.bind((tcp.host, tcp.port))
    sock.listen()
    print(f'[INFO] TCP Server started (HOST={tcp.host}, PORT={tcp.port})')

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
        return struct.pack(tcp.header_format, len(payload_bytes)) + payload_bytes

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
    header_bytes = recv_all(struct.calcsize(tcp.header_format))
    payload_size = struct.unpack(tcp.header_format, header_bytes)[0]
    payload = recv_all(payload_size)
    return json.loads(payload.decode('utf-8'))

def handle(conn: socket.socket, payload: dict):
    global train_rows, pred_rows, model

    match payload.get('type'):
        case payload_type.train_row:
            train_rows.append(payload)
        case payload_type.train_start:
            print(f'[INFO] Starting training...')
            model = ml.train(train_rows, lookback_period=lookback_period)
            print(f'[INFO] Training finished')
            if payload.get('Save'):
                print(f'[INFO] Saving model...')
                joblib.dump(model, 'model.pkl')
                print(f'[INFO] Model saved')
            send(conn, json.dumps({ 'type': payload_type.train_finish }))
        case payload_type.row:
            if not model:
                print('[INFO] Loading model...')
                model = joblib.load('./model.pkl')
                print('[INFO] Model loaded')
            pred_rows.append(payload)
            if len(pred_rows) > lookback_period:
                pred_rows = pred_rows[-(lookback_period+1):]
                df = ml.lookback(ml.preprocess(pd.DataFrame(pred_rows)), period=lookback_period)
                pred_class = int(model.predict(df.iloc[[-1]])[0])
                print(f'[INFO] Prediction: {pred_class}')
                send(conn, json.dumps({'type': payload_type.prediction, 'prediction': pred_class}))
            else:
                send(conn, json.dumps({'type': payload_type.prediction, 'prediction': 0 }))
