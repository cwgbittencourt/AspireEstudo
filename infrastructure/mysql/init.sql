-- Inicializa o schema necessario para o VehicleMySqlRepository.
CREATE DATABASE IF NOT EXISTS appdb CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
USE appdb;

CREATE TABLE IF NOT EXISTS veiculo (
    Id INT NOT NULL,
    Placa VARCHAR(16) NOT NULL,
    DataEvento DATETIME(6) NOT NULL,
    Lat DECIMAL(10,7) NOT NULL,
    Lon DECIMAL(10,7) NOT NULL,
    Velocidade INT NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE KEY IX_veiculo_Placa_DataEvento (Placa, DataEvento)
) ENGINE=InnoDB;
