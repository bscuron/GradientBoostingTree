import server
import time
import model

HISTORICAL_DATA = []
REALTIME_DATA = []

def handle(payload: dict):
    if not hasattr(handle, 'time'):
        handle.time = int(time.time() * 1000)
    global HISTORICAL_DATA, REALTIME_DATA
    PAYLOAD_TYPE = server.PAYLOAD_TYPE
    match payload.get('type'):
        case PAYLOAD_TYPE.PING: return
        case PAYLOAD_TYPE.ROW:
            if payload.get('time') < handle.time:
                HISTORICAL_DATA.append(payload)
            else:
                REALTIME_DATA.append(payload)
        case PAYLOAD_TYPE.TRAIN:
            model.train(HISTORICAL_DATA)
