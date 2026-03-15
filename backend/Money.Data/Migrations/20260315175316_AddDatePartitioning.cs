using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Money.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDatePartitioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {

            migrationBuilder.Sql("""
                DROP INDEX ix_operations_comment;
                DROP INDEX ix_operations_user_id_category_id;
                ALTER TABLE operations DROP CONSTRAINT fk_operations_categories_user_id_category_id;
                ALTER TABLE operations DROP CONSTRAINT fk_operations_domain_users_user_id;
                ALTER TABLE operations DROP CONSTRAINT pk_operations;

                ALTER TABLE operations RENAME TO operations_old;

                CREATE TABLE operations (
                    LIKE operations_old INCLUDING DEFAULTS INCLUDING GENERATED
                ) PARTITION BY RANGE (date);

                ALTER TABLE operations ADD CONSTRAINT pk_operations PRIMARY KEY (user_id, id, date);

                ALTER TABLE operations
                    ADD CONSTRAINT fk_operations_categories_user_id_category_id
                    FOREIGN KEY (user_id, category_id) REFERENCES categories (user_id, id) ON DELETE CASCADE;

                ALTER TABLE operations
                    ADD CONSTRAINT fk_operations_domain_users_user_id
                    FOREIGN KEY (user_id) REFERENCES domain_users (id) ON DELETE CASCADE;

                DO $$
                DECLARE
                    v_start  DATE := COALESCE(
                        (SELECT date_trunc('month', MIN(date)) FROM operations_old),
                        date_trunc('month', now())
                    );
                    v_end    DATE := date_trunc('month', now()) + INTERVAL '3 months';
                    v_month  DATE := v_start;
                    v_next   DATE;
                    v_name   TEXT;
                BEGIN
                    WHILE v_month < v_end LOOP
                        v_next := v_month + INTERVAL '1 month';
                        v_name := 'operations_' || to_char(v_month, 'YYYY_MM');
                        EXECUTE format(
                            'CREATE TABLE %I PARTITION OF operations FOR VALUES FROM (%L) TO (%L)',
                            v_name, v_month::text, v_next::text
                        );
                        v_month := v_next;
                    END LOOP;
                END $$;

                CREATE TABLE operations_default PARTITION OF operations DEFAULT;

                INSERT INTO operations SELECT * FROM operations_old;

                DO $$
                DECLARE
                    old_count bigint;
                    new_count bigint;
                BEGIN
                    SELECT count(*) INTO old_count FROM operations_old;
                    SELECT count(*) INTO new_count FROM operations;
                    IF old_count != new_count THEN
                        RAISE EXCEPTION 'Row count mismatch after partitioning: old=%, new=%', old_count, new_count;
                    END IF;
                END $$;

                DROP TABLE operations_old;

                CREATE INDEX IF NOT EXISTS idx_operations_date ON operations(date);
                CREATE INDEX IF NOT EXISTS idx_operations_user_date ON operations(user_id, date);
                CREATE INDEX IF NOT EXISTS idx_operations_user_category ON operations(user_id, category_id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS idx_operations_date;
                DROP INDEX IF EXISTS idx_operations_user_date;
                DROP INDEX IF EXISTS idx_operations_user_category;

                ALTER TABLE operations DROP CONSTRAINT IF EXISTS fk_operations_categories_user_id_category_id;
                ALTER TABLE operations DROP CONSTRAINT IF EXISTS fk_operations_domain_users_user_id;
                ALTER TABLE operations DROP CONSTRAINT IF EXISTS pk_operations;

                ALTER TABLE operations RENAME TO operations_partitioned;

                CREATE TABLE operations (
                    LIKE operations_partitioned INCLUDING DEFAULTS INCLUDING GENERATED
                );

                ALTER TABLE operations ADD CONSTRAINT pk_operations PRIMARY KEY (user_id, id);

                ALTER TABLE operations
                    ADD CONSTRAINT fk_operations_categories_user_id_category_id
                    FOREIGN KEY (user_id, category_id) REFERENCES categories (user_id, id) ON DELETE CASCADE;

                ALTER TABLE operations
                    ADD CONSTRAINT fk_operations_domain_users_user_id
                    FOREIGN KEY (user_id) REFERENCES domain_users (id) ON DELETE CASCADE;

                INSERT INTO operations
                SELECT DISTINCT ON (user_id, id) user_id, id, date, created_task_id, sum, category_id, comment, place_id, is_deleted
                FROM operations_partitioned
                ORDER BY user_id, id, date;

                DO $$
                DECLARE
                    partitioned_count bigint;
                    new_count bigint;
                BEGIN
                    SELECT count(*) INTO partitioned_count FROM operations_partitioned;
                    SELECT count(*) INTO new_count FROM operations;
                    IF partitioned_count != new_count THEN
                        RAISE EXCEPTION 'Row count mismatch during rollback: partitioned=%, new=%', partitioned_count, new_count;
                    END IF;
                END $$;

                DROP TABLE operations_partitioned CASCADE;

                CREATE INDEX IF NOT EXISTS ix_operations_comment ON operations(comment);
                CREATE INDEX IF NOT EXISTS ix_operations_user_id_category_id ON operations(user_id, category_id);
                """);
        }
    }
}
