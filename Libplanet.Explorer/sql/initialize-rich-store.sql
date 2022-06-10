CREATE TABLE IF NOT EXISTS `tx_references` (
    CONSTRAINT `uid` UNIQUE (`tx_id`, `block_hash`),

    `tx_id`         BINARY(32),
    `block_hash`    BINARY(32),
    `tx_nonce`      BIGINT
);

CREATE TABLE IF NOT EXISTS `signer_references` (
    CONSTRAINT `uid` UNIQUE (`signer`, `tx_id`),

    `signer`    BINARY(20),
    `tx_id`     BINARY(32),
    `tx_nonce`  BIGINT
);

CREATE TABLE IF NOT EXISTS `updated_address_references` (
    CONSTRAINT `uid` UNIQUE (`updated_address`, `tx_id`),

    `updated_address`   BINARY(20),
    `tx_id`             BINARY(32),
    `tx_nonce`          BIGINT
);

CREATE TABLE IF NOT EXISTS `blocks` (
    CONSTRAINT `uid` UNIQUE (`hash`),

    `difficulty`            BIGINT,
    `hash`                  VARCHAR(64),
    `hash_algorithm`        VARCHAR(100),
    `index`                 BIGINT,
    `miner`                 VARCHAR(42),
    `nonce`                 VARCHAR(20),
    `pre_evaluation_hash`   VARCHAR(128),
    `previous_hash`         VARCHAR(64),
    `protocol_version`      TINYINT UNSIGNED,
    `public_key`            VARCHAR(128),
    `signature`             VARCHAR(128),
    `state_root_hash`       VARCHAR(64),
    `timestamp`             DATETIME(6),
    `total_difficulty`      BIGINT,
    `tx_hash`               VARCHAR(64)
);

CREATE TABLE IF NOT EXISTS `actions` (
    CONSTRAINT `uid` UNIQUE (`tx_id`, `tx_nonce`),

    `action`                LONGTEXT,
    `tx_id`                 VARCHAR(64),
    `tx_nonce`              BIGINT
);
