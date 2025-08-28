import numpy as np
import pandas as pd
from sklearn.model_selection import train_test_split
from sklearn.metrics import accuracy_score, roc_auc_score, classification_report
from lightgbm import LGBMClassifier, early_stopping, log_evaluation

def train(rows=None, lookback_period=5, undersample_ratio=3):
    df_unprocessed = pd.DataFrame(rows)
    print(f'[INFO] Training Data (Unprocessed): {df_unprocessed}')
    
    df_processed = lookback(preprocess(df_unprocessed.copy()), period=lookback_period)
    print(f'[INFO] Features: {df_processed.columns}')
    print(f'[INFO] Training Data (Processed): {df_processed}')
    
    df_labels = find_swings(df_unprocessed, 25).iloc[lookback_period:].reset_index(drop=True)
    
    df_train = pd.concat([df_processed, df_labels], axis=1)
    
    df_train_major = df_train[df_train.label == 0]
    df_train_minor = df_train[df_train.label != 0]
    
    n_major_sample = min(len(df_train_major), len(df_train_minor) * undersample_ratio)
    df_train_major_sampled = df_train_major.sample(n=n_major_sample, random_state=42)
    
    df_train_balanced = pd.concat([df_train_minor, df_train_major_sampled]).sort_index().reset_index(drop=True)
    
    X_train_balanced = df_train_balanced.drop('label', axis=1)
    y_train_balanced = df_train_balanced['label']
    
    split_idx = int(len(X_train_balanced) * 0.8)
    X_train, X_valid = X_train_balanced.iloc[:split_idx], X_train_balanced.iloc[split_idx:]
    y_train, y_valid = y_train_balanced.iloc[:split_idx], y_train_balanced.iloc[split_idx:]
    
    class_counts = y_train.value_counts().to_dict()
    max_count = max(class_counts.values())
    
    # TODO: Research Bayesian Optimization or Gradient-free search
    model = LGBMClassifier(
        objective='multiclass',
        num_class=3,
        n_estimators=int(25e3),
        class_weight={0:10, 1:1, 2:1},
        learning_rate=0.05,
        num_leaves=31,
        subsample=0.8,
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
    feat_df = pd.DataFrame({'feature': feature_names, 'importance': importances}).sort_values(by='importance', ascending=False)
    print(feat_df)
    
    return model

def lookback(df: pd.DataFrame, period: int = 5) -> pd.DataFrame:
    lagged_cols = []

    for col in df.columns:
        for lag in range(1, period + 1):
            lagged_cols.append(df[col].shift(lag).rename(f'{col}_{lag}'))

    if lagged_cols:
        df_lags = pd.concat(lagged_cols, axis=1)
        df = pd.concat([df, df_lags], axis=1)

    df = df.dropna().reset_index(drop=True)
    return df

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

def preprocess(df: pd.DataFrame) -> pd.DataFrame:
    eps = 1e-6

    df['candle_body'] = df['close'] - df['open']
    df['candle_body_size'] = df['candle_body'].abs()
    df['candle_range'] = df['high'] - df['low']

    df['candle_body_to_range_ratio'] = df['candle_body_size'] / (df['candle_range'] + eps)
    df['candle_upperwick'] = df['high'] - df[['open', 'close']].max(axis=1)
    df['candle_lowerwick'] = df[['open', 'close']].min(axis=1) - df['low']

    total_wick = df['candle_upperwick'] + df['candle_lowerwick']
    df['candle_body_to_wick_ratio'] = df['candle_body_size'] / (total_wick + eps)
    df['candle_body_to_volume_ratio'] = df['candle_body_size'] / (df['volume'] + eps)
    df['candle_wick_ratio'] = df['candle_upperwick'] / (df['candle_lowerwick'] + eps)
    df['candle_wick_to_range_ratio'] = total_wick / (df['candle_range'] + eps)
    df['candle_upperwick_to_range_ratio'] = df['candle_upperwick'] / (df['candle_range'] + eps)
    df['candle_lowerwick_to_range_ratio'] = df['candle_lowerwick'] / (df['candle_range'] + eps)
    df['candle_wick_to_volume_ratio'] = total_wick / (df['volume'] + eps)
    df['candle_upperwick_to_volume_ratio'] = df['candle_upperwick'] / (df['volume'] + eps)
    df['candle_lowerwick_to_volume_ratio'] = df['candle_lowerwick'] / (df['volume'] + eps)

    return df.dropna().drop(['type', 'time', 'open', 'high', 'low', 'close', 'volume'], axis=1)
