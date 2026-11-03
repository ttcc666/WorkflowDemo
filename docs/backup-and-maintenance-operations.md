# 三库备份、还原与维护运维

数据库备份由宿主运维工具执行，框架不读取业务连接并自动备份/还原。以下命令是专用演练库示例，需替换主机、授权账号和路径；未针对真实业务库运行。还原目的库应为新建 `approval_test_restore`，避免覆盖已有数据。

## 一致性与准备

1. 记录应用版本、迁移链、宿主目录/身份版本、保留期、权限适配和恢复目标（RPO/RTO）。
2. 停止所有 writer、Outbox/超时/维护 worker；确认没有仍在修改的旧版本实例。
3. 备份数据库、附件、归档存储以及解密/签名所需配置，使用同一截止点；保存独立校验清单。
4. 备份文件与敏感归档须加密并限制账号权限。密码使用工具提示或受限进程环境，不能写入命令历史、日志或制品。
5. 在隔离网络还原数据库和附件，暂不启动外部渠道及破坏性维护，先做只读核对。

仅复制数据库不能保证附件一致，也不能撤销已经成功发送的邮件/HTTP。重新投递仍需稳定 Outbox ID 与接收方防重。迁移涉及 MySQL DDL 时，不能将事务包裹视为整段 schema 的可回滚保证。

## MySQL 8.4 / InnoDB

暂停 DDL；全表为 InnoDB 时使用一致性事务快照。示例 CLI（密码由交互提示输入）：

```powershell
mysqldump --host '127.0.0.1' --port '3306' --user 'backup_operator' --password --single-transaction --routines --triggers --events --set-gtid-purged='OFF' --no-tablespaces --default-character-set='utf8mb4' --result-file='D:/backup/approval.sql' 'approval_test_source'
mysql --host '127.0.0.1' --port '3306' --user 'restore_operator' --password --execute='CREATE DATABASE approval_test_restore CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin;'
mysql --host '127.0.0.1' --port '3306' --user 'restore_operator' --password --database='approval_test_restore' --execute='SOURCE D:/backup/approval.sql'
```

导出未加 `--databases`，避免导入时自动创建/切换到原业务库。核对 `__EFMigrationsHistory`、表引擎、字符集、外键/唯一约束及 schema 摘要；禁止凭恢复退出码零就开放 writer。非事务表或并发 DDL 要另定锁定与备份流程。

## SQL Server 2022 / compatibility 160

使用专用 backup operator，在 SQL Server 可访问的目录执行；路径是服务器路径。SQL 示例：

```sql
BACKUP DATABASE [approval_test_source]
TO DISK = N'C:\Backups\approval_test_source.bak'
WITH COPY_ONLY, CHECKSUM, COMPRESSION;
RESTORE VERIFYONLY FROM DISK = N'C:\Backups\approval_test_source.bak' WITH CHECKSUM;
RESTORE FILELISTONLY FROM DISK = N'C:\Backups\approval_test_source.bak';
```

根据 FILELISTONLY 返回的真实 logical names 填写 MOVE，再还原到新库：

```sql
RESTORE DATABASE [approval_test_restore]
FROM DISK = N'C:\Backups\approval_test_source.bak'
WITH MOVE N'<实际 data logical name>' TO N'C:\Restore\approval_test_restore.mdf',
     MOVE N'<实际 log logical name>' TO N'C:\Restore\approval_test_restore_log.ldf',
     CHECKSUM;
DBCC CHECKDB ([approval_test_restore]) WITH NO_INFOMSGS;
SELECT compatibility_level FROM sys.databases WHERE name = N'approval_test_restore';
```

不使用 REPLACE，不覆盖现有库。VERIFYONLY 不是完整业务恢复验收；另核对用户映射、证书/加密密钥、数据库选项、MARS 配置和权限。

## PostgreSQL 17

使用匹配大版本的 pg_dump/pg_restore；账号、权限与扩展单独记录。新库演练：

```powershell
pg_dump --host '127.0.0.1' --port '5432' --username 'backup_operator' --password --format='custom' --file='D:/backup/approval.dump' 'approval_test_source'
pg_restore --list 'D:/backup/approval.dump'
createdb --host '127.0.0.1' --port '5432' --username 'restore_operator' --password 'approval_test_restore'
pg_restore --host '127.0.0.1' --port '5432' --username 'restore_operator' --password --exit-on-error --single-transaction --no-owner --no-acl --dbname='approval_test_restore' 'D:/backup/approval.dump'
```

`--no-owner --no-acl` 用于隔离演练，不等于生产权限恢复。生产应按审批后的角色/所有者清单还原，不能从未受信任的备份内容直接扩大账号权限。检查扩展、collation、UTC 时间、约束和序列。

## 必须观察的恢复结果

- 迁移和模型无漂移，单宿主迁移审计正确；宿主查询范围、非参与人详情和文件访问继续拒绝。
- 运行中实例仍绑定原定义/Schema，能继续审批，业务占用与状态一致。
- 同键提交返回原结果或原键 tombstone 的 410；不能由于还原而先删防重记录再重做。
- 未完成维护任务只从已提交批次恢复，批次数量与快照/在线数据匹配；失败批次没有半份归档。
- 附件按数据库中的存储键与 SHA256/大小核对；排队删除、保留、已过期详情状态与备份截止点一致。
- 外部投递先禁用真实接收方，用替身渠道验证原消息 ID；确认可能重复投递的对账方案后才恢复真实渠道。

记录恢复时间、查询/制品摘要、测试数量和人工检查结果。三库临时容器的事务/迁移测试只证明相应用例，不替代上述备份文件、附件与外部业务对账演练。

## 告警与保留

本机真实服务演练现已完成三库原生备份、新库完整还原、附件摘要以及审批/维护继续，见[演练入口与结果](live-service-rehearsals.md)。这不是生产备份权限、密钥、账号或外部对账恢复的验收。

使用 `/diagnostics` 做企业内详细观察，匿名 `/health/approval` 只做共享库就绪检查。告警起点由宿主 SLO 决定：最老 Outbox 持续超过投递目标、失败数上升、后台心跳久未完成、超期任务/无合格处理人、业务占用不一致、文件删除重试或维护游标不推进。Outbox 已取消租约，不再以投递租约过期作为告警项；维护及附件删除也已移除持久租约，使用单进程执行和乐观并发。

处理告警先只读核对，重派/重试/保留解除使用独立权限。维护失败和策略冲突不能通过清表、重置游标、清空 ChangeTracker 或换幂等键“恢复”。设置保留后验证预览不再选中；释放前确认允许后续批次处理。

先配置每种记录的保留期并在副本预览，再小批量验证：ArchiveAfter 与 PurgeAfter 分离；OperationsAfter 表示结果重放窗口；ArchivedPayloadsAfter 控制详细历史及关联附件。永久命令防重和归档标记不按普通年龄删除。不要在事故处理中临时开启大范围删除。

参考：[MySQL mysqldump](https://dev.mysql.com/doc/refman/8.4/en/mysqldump.html)、[SQL Server 备份](https://learn.microsoft.com/en-us/sql/relational-databases/backup-restore/back-up-and-restore-of-sql-server-databases)、[PostgreSQL 备份](https://www.postgresql.org/docs/17/backup.html)。
