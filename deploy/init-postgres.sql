-- AlpacaMock PostgreSQL/TimescaleDB Initialization
-- This script creates all required tables for market data storage

-- Enable TimescaleDB extension
CREATE EXTENSION IF NOT EXISTS timescaledb;

-- Symbols reference table
CREATE TABLE IF NOT EXISTS symbols (
    symbol VARCHAR(20) PRIMARY KEY,
    name VARCHAR(255),
    exchange VARCHAR(20),
    asset_class VARCHAR(20) DEFAULT 'us_equity',
    status VARCHAR(20) DEFAULT 'active',
    tradable BOOLEAN DEFAULT TRUE,
    shortable BOOLEAN DEFAULT TRUE,
    fractionable BOOLEAN DEFAULT TRUE,
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Create index on status for filtering
CREATE INDEX IF NOT EXISTS idx_symbols_status ON symbols(status);

-- Minute bars (main table for backtesting)
CREATE TABLE IF NOT EXISTS bars_minute (
    time TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    open NUMERIC(18, 6) NOT NULL,
    high NUMERIC(18, 6) NOT NULL,
    low NUMERIC(18, 6) NOT NULL,
    close NUMERIC(18, 6) NOT NULL,
    volume BIGINT NOT NULL,
    vwap NUMERIC(18, 6),
    transactions INTEGER,
    PRIMARY KEY (time, symbol)
);

-- Convert to hypertable for efficient time-series queries
SELECT create_hypertable('bars_minute', 'time',
    chunk_time_interval => INTERVAL '1 day',
    if_not_exists => TRUE);

-- Create indexes for common query patterns
CREATE INDEX IF NOT EXISTS idx_bars_minute_symbol_time ON bars_minute(symbol, time DESC);

-- Hourly bars (aggregated from minute data)
CREATE TABLE IF NOT EXISTS bars_hour (
    time TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    open NUMERIC(18, 6) NOT NULL,
    high NUMERIC(18, 6) NOT NULL,
    low NUMERIC(18, 6) NOT NULL,
    close NUMERIC(18, 6) NOT NULL,
    volume BIGINT NOT NULL,
    vwap NUMERIC(18, 6),
    transactions INTEGER,
    PRIMARY KEY (time, symbol)
);

SELECT create_hypertable('bars_hour', 'time',
    chunk_time_interval => INTERVAL '1 week',
    if_not_exists => TRUE);

CREATE INDEX IF NOT EXISTS idx_bars_hour_symbol_time ON bars_hour(symbol, time DESC);

-- Daily bars
CREATE TABLE IF NOT EXISTS bars_daily (
    time TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    open NUMERIC(18, 6) NOT NULL,
    high NUMERIC(18, 6) NOT NULL,
    low NUMERIC(18, 6) NOT NULL,
    close NUMERIC(18, 6) NOT NULL,
    volume BIGINT NOT NULL,
    vwap NUMERIC(18, 6),
    transactions INTEGER,
    PRIMARY KEY (time, symbol)
);

SELECT create_hypertable('bars_daily', 'time',
    chunk_time_interval => INTERVAL '1 month',
    if_not_exists => TRUE);

CREATE INDEX IF NOT EXISTS idx_bars_daily_symbol_time ON bars_daily(symbol, time DESC);

-- Weekly bars
CREATE TABLE IF NOT EXISTS bars_weekly (
    time TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    open NUMERIC(18, 6) NOT NULL,
    high NUMERIC(18, 6) NOT NULL,
    low NUMERIC(18, 6) NOT NULL,
    close NUMERIC(18, 6) NOT NULL,
    volume BIGINT NOT NULL,
    vwap NUMERIC(18, 6),
    transactions INTEGER,
    PRIMARY KEY (time, symbol)
);

SELECT create_hypertable('bars_weekly', 'time',
    chunk_time_interval => INTERVAL '1 year',
    if_not_exists => TRUE);

CREATE INDEX IF NOT EXISTS idx_bars_weekly_symbol_time ON bars_weekly(symbol, time DESC);

-- Monthly bars
CREATE TABLE IF NOT EXISTS bars_monthly (
    time TIMESTAMPTZ NOT NULL,
    symbol VARCHAR(20) NOT NULL,
    open NUMERIC(18, 6) NOT NULL,
    high NUMERIC(18, 6) NOT NULL,
    low NUMERIC(18, 6) NOT NULL,
    close NUMERIC(18, 6) NOT NULL,
    volume BIGINT NOT NULL,
    vwap NUMERIC(18, 6),
    transactions INTEGER,
    PRIMARY KEY (time, symbol)
);

SELECT create_hypertable('bars_monthly', 'time',
    chunk_time_interval => INTERVAL '5 years',
    if_not_exists => TRUE);

CREATE INDEX IF NOT EXISTS idx_bars_monthly_symbol_time ON bars_monthly(symbol, time DESC);

-- Data coverage tracking
CREATE TABLE IF NOT EXISTS data_coverage (
    symbol VARCHAR(20) NOT NULL,
    resolution VARCHAR(20) NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    last_updated TIMESTAMPTZ DEFAULT NOW(),
    PRIMARY KEY (symbol, resolution)
);

-- Insert some sample symbols for testing
INSERT INTO symbols (symbol, name, exchange, status) VALUES
    ('AAPL', 'Apple Inc.', 'NASDAQ', 'active'),
    ('MSFT', 'Microsoft Corporation', 'NASDAQ', 'active'),
    ('GOOGL', 'Alphabet Inc.', 'NASDAQ', 'active'),
    ('AMZN', 'Amazon.com Inc.', 'NASDAQ', 'active'),
    ('TSLA', 'Tesla Inc.', 'NASDAQ', 'active'),
    ('META', 'Meta Platforms Inc.', 'NASDAQ', 'active'),
    ('NVDA', 'NVIDIA Corporation', 'NASDAQ', 'active'),
    ('SPY', 'SPDR S&P 500 ETF', 'NYSE', 'active'),
    ('QQQ', 'Invesco QQQ Trust', 'NASDAQ', 'active'),
    ('IWM', 'iShares Russell 2000 ETF', 'NYSE', 'active')
ON CONFLICT (symbol) DO NOTHING;

-- Insert sample bar data for testing (AAPL, last 5 days of mock data)
INSERT INTO bars_daily (time, symbol, open, high, low, close, volume, vwap, transactions) VALUES
    (NOW() - INTERVAL '5 days', 'AAPL', 178.50, 180.00, 177.50, 179.25, 50000000, 178.90, 250000),
    (NOW() - INTERVAL '4 days', 'AAPL', 179.25, 181.50, 178.00, 180.75, 48000000, 179.80, 240000),
    (NOW() - INTERVAL '3 days', 'AAPL', 180.75, 182.00, 179.50, 181.50, 52000000, 180.90, 260000),
    (NOW() - INTERVAL '2 days', 'AAPL', 181.50, 183.25, 180.25, 182.00, 47000000, 181.80, 235000),
    (NOW() - INTERVAL '1 day', 'AAPL', 182.00, 184.00, 181.00, 183.50, 55000000, 182.50, 275000)
ON CONFLICT (time, symbol) DO NOTHING;

-- Grant permissions
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO postgres;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO postgres;

-- Verify setup
DO $$
BEGIN
    RAISE NOTICE 'AlpacaMock database initialized successfully!';
    RAISE NOTICE 'Tables created: symbols, bars_minute, bars_hour, bars_daily, bars_weekly, bars_monthly, data_coverage';
END $$;
