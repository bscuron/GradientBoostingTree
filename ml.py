import numpy as np
import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, roc_auc_score, classification_report
from lightgbm import LGBMClassifier, early_stopping, log_evaluation

def train(data=None, lookback_period=5):
    df_unprocessed = pd.DataFrame(data)
    print(f'[INFO] Training Data: {df_unprocessed}')
    df_processed = lookback(preprocess(df_unprocessed), period=lookback_period)
    print(f'[INFO] Features: {df_processed.columns}')
    print(f'[INFO] Training Data (Normalized): {df_processed}')
    df_labels = find_swings(df_unprocessed, 25).iloc[lookback_period:].reset_index(drop=True)
    X_train, X_valid, y_train, y_valid = train_test_split(
        df_processed, df_labels, test_size=0.2, random_state=42, shuffle=False
    )

    class_counts = y_train.value_counts().to_dict()
    max_count = max(class_counts.values())
    class_weight = {cls: int(max_count / count) for cls, count in class_counts.items()}
    model = LGBMClassifier(
        objective='multiclass',
        num_class=3,
        n_estimators=int(25e3),
        learning_rate=0.1,
        num_leaves=31,
        subsample=0.8,
        class_weight=class_weight,
        colsample_bytree=0.8,
        random_state=42
    )

    model.fit(
        X_train, y_train,
        eval_set=[(X_valid, y_valid)],
        eval_metric='multi_logloss',
        callbacks=[early_stopping(stopping_rounds=50), log_evaluation(period=50)],
    )

    pred = model.predict(X_valid)
    proba = model.predict_proba(X_valid)
    print(proba)
    print(accuracy_score(y_valid, pred))
    print(roc_auc_score(y_valid, proba, multi_class='ovr'))
    print(classification_report(y_valid, pred, target_names=['No Swing', 'Swing High', 'Swing Low']))

    importances = model.feature_importances_
    feature_names = X_train.columns
    feat_df = pd.DataFrame({
        'feature': feature_names,
        'importance': importances
    }).sort_values(by='importance', ascending=False)
    print(feat_df)
    return model

def lookback(df: pd.DataFrame, period: int = 5) -> pd.DataFrame:
    dfc = df.copy()
    lagged_cols = []

    for col in df.columns:
        for lag in range(1, period + 1):
            lagged_cols.append(df[col].shift(lag).rename(f'{col}_{lag}'))

    if lagged_cols:
        df_lags = pd.concat(lagged_cols, axis=1)
        dfc = pd.concat([dfc, df_lags], axis=1)

    dfc = dfc.dropna().reset_index(drop=True)
    return dfc

def find_swings(data, strength=5):
    highs = data['high'].values
    lows = data['low'].values

    labels = [0] * len(data)
    for i in range(strength, len(data) - strength):
        if highs[i] == max(highs[i - strength : i + strength + 1]):
            labels[i] = 1
        elif lows[i] == min(lows[i - strength : i + strength + 1]):
            labels[i] = 2

    return pd.Series(labels, name='label')

def preprocess(data: pd.DataFrame) -> pd.DataFrame:
    data_numeric = data.drop(['type', 'time', 'high', 'low', 'close'], axis=1)
    return pd.concat([data_numeric], axis=1)
